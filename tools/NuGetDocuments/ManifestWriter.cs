using System.Text.Json;

internal static class ManifestWriter
{
    public static int Write(string output, PackageResult[] results)
    {
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(results.Select(r => r.Manifest),
            new JsonSerializerOptions { WriteIndented = true }) + "\n");
        var errors = results.Where(r => r.HasErrors).ToArray();
        Console.WriteLine($"Collected {results.Length} packages; {errors.Length} packages have unresolved collection errors (manifest.json).");
        if (errors.Length != 0)
        {
            Console.Write($"Unresolved packages below:\n{string.Join("\n", errors.Select(r => $"{r.Id}: v{r.Version}"))}\n");
        }
        return errors.Length == 0 ? 0 : 2;
    }
}
