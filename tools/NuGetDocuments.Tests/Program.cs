using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

var root = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "nuget-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    await ResolverTests.Run();
    var cache = Path.Combine(root, "cache");
    foreach (var name in new[] { "Bundled", "Unsafe" })
    {
        var package = Path.Combine(cache, name.ToLowerInvariant(), "1.0.0", name.ToLowerInvariant() + ".1.0.0.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(package)!);
        using var archive = ZipFile.Open(package, ZipArchiveMode.Create);
        Write(archive, name + ".nuspec", "<package><metadata><id>" + name + "</id><version>1.0.0</version><repository url=\"https://example.org/not-github\" /></metadata></package>");
        if (name == "Bundled") Write(archive, "LICENSE", "Original bundled license");
    }
    var inventory = Path.Combine(root, "inventory.json");
    File.WriteAllText(inventory, JsonSerializer.Serialize(new { projects = new[] { new { frameworks = new[] { new { topLevelPackages = new[] { "Bundled", "Unsafe" }.Select(id => new { id, resolvedVersion = "1.0.0" }) } } } } }));
    var output = Path.Combine(root, "output");
    Check(await Collector.Run([inventory, cache, output]) == 2, "unresolved collection must exit 2");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
    var bundled = manifest.RootElement[0];
    var unsafePackage = manifest.RootElement[1];
    Check(bundled.GetProperty("status").GetString() == "original-license-collected" && bundled.GetProperty("sourceResolution").ValueKind == JsonValueKind.Null, "bundled provenance");
    Check(File.ReadAllText(Path.Combine(output, "packages/bundled/1.0.0/LICENSE")) == "Original bundled license", "original bytes");
    Check(unsafePackage.GetProperty("status").GetString() == "error" && unsafePackage.GetProperty("sourceAttempts").GetArrayLength() == 0, "unsafe repository");
    Check(unsafePackage.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToHashSet().SetEquals(new[] { "unsafe-or-unsupported-repository", "unresolved-original-license" }), "error codes");
    var firstManifest = File.ReadAllBytes(Path.Combine(output, "manifest.json"));
    Check(await Collector.Run([inventory, cache, output]) == 2, "repeat unresolved collection must exit 2");
    Check(firstManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(output, "manifest.json"))), "parallel collector manifest must be byte-deterministic");
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false };
    foreach (var argument in new[] { typeof(Collector).Assembly.Location, inventory, cache, output }) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();
    Check(process.ExitCode == 2, "real console entry point must exit 2");
    Check(firstManifest.SequenceEqual(File.ReadAllBytes(Path.Combine(output, "manifest.json"))), "CLI and shared collector must produce identical manifests");
    Console.WriteLine("PASS full collector bundled license + unsafe repository + real CLI exit 2 + repeat byte-deterministic manifest");
}
finally { Directory.Delete(root, true); }

static void Check(bool value, string name) { if (!value) throw new Exception(name); }
static void Write(ZipArchive archive, string name, string text)
{
    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
    writer.Write(text);
}
