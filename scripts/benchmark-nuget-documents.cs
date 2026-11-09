#:property PublishAot=false
using System.Diagnostics;
using System.Text.Json.Nodes;

// dotnet run --file scripts/benchmark-nuget-documents.cs -- BEFORE.cs AFTER.cs INVENTORY CACHE OUTPUT
// Real restored archives only. Compilation/startup are excluded from the emitted collector timings.
if (args.Length != 5) throw new ArgumentException("Expected BEFORE.cs AFTER.cs INVENTORY CACHE OUTPUT");
var output = Path.GetFullPath(args[4]);
Directory.CreateDirectory(output);
foreach (var (sourcePath, label) in new[] { (args[0], "before"), (args[1], "after") })
{
    var source = File.ReadAllText(sourcePath);
    source = source.Replace("// Preserve original documents", "var benchmarkClock = System.Diagnostics.Stopwatch.StartNew();\n// Preserve original documents")
        .Replace("new HttpClientHandler { AllowAutoRedirect = false }", "new BenchmarkHandler()")
        .Replace("Environment.ExitCode = unresolved == 0 ? 0 : 2;", "Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { seconds = benchmarkClock.Elapsed.TotalSeconds, requests = BenchmarkHandler.Requests, transferredBytes = BenchmarkHandler.Bytes }));\nEnvironment.ExitCode = unresolved == 0 ? 0 : 2;");
    source += """

class BenchmarkHandler : DelegatingHandler
{
    public static int Requests;
    public static long Bytes;
    public BenchmarkHandler() : base(new HttpClientHandler { AllowAutoRedirect = false }) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Interlocked.Increment(ref Requests);
        var response = await base.SendAsync(request, token);
        // Benchmark-only buffering accounts for actual response-body bytes, including 404s.
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        Interlocked.Add(ref Bytes, bytes.LongLength);
        return response;
    }
}
""";
    var instrumented = Path.Combine(output, label + ".cs");
    File.WriteAllText(instrumented, source);
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, RedirectStandardOutput = true };
    foreach (var argument in new[] { "run", "--file", instrumented, "--", Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), Path.Combine(output, label) }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var log = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    File.WriteAllText(Path.Combine(output, label + ".log"), log);
    Console.WriteLine(label + ": " + log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
    if (process.ExitCode != 0) throw new Exception(label + " collection failed: " + process.ExitCode);
}
var before = Path.Combine(output, "before");
var after = Path.Combine(output, "after");
var expectedFiles = Directory.GetFiles(before, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(before, p)).Order(StringComparer.Ordinal).ToArray();
var actualFiles = Directory.GetFiles(after, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(after, p)).Order(StringComparer.Ordinal).ToArray();
if (!expectedFiles.SequenceEqual(actualFiles)) throw new Exception("Document file set changed");
foreach (var path in expectedFiles)
{
    if (path == "manifest.json") continue;
    if (!File.ReadAllBytes(Path.Combine(before, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(after, path)))) throw new Exception("Original bytes changed: " + path);
}
JsonNode Normalize(string path)
{
    var rows = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
    foreach (var row in rows) row!.AsObject().Remove("sourceAttempts");
    return rows;
}
if (!JsonNode.DeepEquals(Normalize(Path.Combine(before, "manifest.json")), Normalize(Path.Combine(after, "manifest.json")))) throw new Exception("Manifest changed beyond sourceAttempts discovery metadata");
Console.WriteLine($"PASS identical {expectedFiles.Length - 1} original files and manifests (excluding sourceAttempts only)");
