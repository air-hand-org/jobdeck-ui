internal static class Collector
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 3)
        {
            throw new ArgumentException("Expected INVENTORY CACHE OUTPUT");
        }

        var output = Path.GetFullPath(args[2]);
        // Validate all archives before replacing the scoped generated output.
        var archives = PackageInventory.Read(args[0], args[1]);
        if (Directory.Exists(output))
        {
            Directory.Delete(output, true);
        }

        Directory.CreateDirectory(output);
        var results = new PackageResult[archives.Length];
        var completed = 0;
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        var resolver = new SourceResolver(path => GitHubClient.GetApi(http, path,
            Environment.GetEnvironmentVariable("GITHUB_TOKEN")), url => GitHubClient.Download(http, url));
        using (var progress = new ConsoleProgress())
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, archives.Length),
                new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (index, cancellationToken) =>
            {
                results[index] = await PackageReader.Read(archives[index], output, resolver);
                lock (progress)
                {
                    progress.Write(++completed, archives.Length);
                }
            });
        }
        return ManifestWriter.Write(output, results);
    }
}
