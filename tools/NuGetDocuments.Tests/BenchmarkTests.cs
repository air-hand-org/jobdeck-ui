using System.Diagnostics;

public class BenchmarkTests
{
    // Opt-in: authentic restored archives and network access are required.
    [Fact(Explicit = true)]
    public async Task LiveCollectorsProduceIdenticalOutputs()
    {
        var args = new[] { "BEFORE", "AFTER", "INVENTORY", "CACHE", "OUTPUT" }
            .Select(name => Environment.GetEnvironmentVariable("NUGET_BENCHMARK_" + name)
                ?? throw new InvalidOperationException("Set NUGET_BENCHMARK_" + name))
            .ToArray();
        var output = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(output);
        foreach (var (sourcePath, label) in new[] { (args[0], "before"), (args[1], "after") })
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in new[] { "run", sourcePath.EndsWith(".csproj", StringComparison.Ordinal) ? "--project" : "--file", Path.GetFullPath(sourcePath), "--", Path.GetFullPath(args[2]), Path.GetFullPath(args[3]), Path.Combine(output, label) })
            {
                start.ArgumentList.Add(argument);
            }

            var clock = Stopwatch.StartNew();
            using var process = Process.Start(start)!;
            var log = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            File.WriteAllText(Path.Combine(output, label + ".log"), log);
            Console.WriteLine($"{label}: {clock.Elapsed.TotalSeconds:F3}s wall clock (includes compilation/startup)");
            if (process.ExitCode != 0)
            {
                throw new Exception(label + " collection failed: " + process.ExitCode);
            }
        }
        var before = Path.Combine(output, "before");
        var after = Path.Combine(output, "after");
        var expectedFiles = Directory.GetFiles(before, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(before, p)).Order(StringComparer.Ordinal).ToArray();
        var actualFiles = Directory.GetFiles(after, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(after, p)).Order(StringComparer.Ordinal).ToArray();
        if (!expectedFiles.SequenceEqual(actualFiles))
        {
            throw new Exception("Document file set changed");
        }

        foreach (var path in expectedFiles)
        {
            if (!File.ReadAllBytes(Path.Combine(before, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(after, path))))
            {
                throw new Exception("Original bytes changed: " + path);
            }
        }
        Console.WriteLine($"PASS identical {expectedFiles.Length} files, including the complete manifest");
    }
}
