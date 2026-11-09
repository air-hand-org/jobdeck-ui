using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

internal record Archive(string Key, (string Id, string Version) Value, string Path);

internal static class PackageInventory
{
    public static Archive[] Read(string inventoryPath, string cache)
    {
        var inventory = JsonSerializer.Deserialize<PackageInventoryDocument>(File.ReadAllText(inventoryPath))
            ?? throw new InvalidDataException("Invalid package inventory");
        cache = Path.GetFullPath(cache);
        var packages = new SortedDictionary<string, (string Id, string Version)>(StringComparer.Ordinal);
        foreach (var project in inventory.Projects)
        {
            foreach (var framework in project.Frameworks)
            {
                foreach (var rows in new[] { framework.TopLevelPackages, framework.TransitivePackages })
                {
                    if (rows == null)
                    {
                        throw new InvalidDataException("Invalid package group");
                    }

                    foreach (var row in rows)
                    {
                        var id = row.Id;
                        var version = row.ResolvedVersion;
                        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z") ||
                            !Regex.IsMatch(version, @"\A[0-9][A-Za-z0-9.+-]*\z"))
                        {
                            throw new InvalidDataException("Invalid package identity");
                        }

                        packages[id.ToLowerInvariant() + "/" + version.ToLowerInvariant()] = (id, version);
                    }
                }
            }
        }

        if (packages.Count == 0)
        {
            throw new InvalidDataException("Empty package inventory");
        }
        // Validate all inputs before replacing the scoped generated output.
        var archives = packages.Select(p => new Archive(p.Key, p.Value, Path.Combine(cache, p.Key,
            p.Value.Id.ToLowerInvariant() + "." + p.Value.Version.ToLowerInvariant() + ".nupkg"))).ToArray();
        foreach (var archive in archives)
        {
            var current = new FileInfo(archive.Path) as FileSystemInfo;
            while (current != null)
            {
                if (current.LinkTarget != null)
                {
                    throw new InvalidDataException("Symlink cache input");
                }

                current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
            }
            using var zip = ZipFile.OpenRead(archive.Path);
            foreach (var entry in zip.Entries)
            {
                PackageReader.SafePath(entry.FullName);
            }
        }
        return archives;
    }
}
