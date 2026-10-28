#:property PublishAot=false
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Trial only: original documents from restored nupkg files, never SPDX templates.
var inventory = JsonDocument.Parse(File.ReadAllText(args[0]));
var cache = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var packages = new SortedDictionary<string, (string Id, string Version)>(StringComparer.Ordinal);
foreach (var project in inventory.RootElement.GetProperty("projects").EnumerateArray())
foreach (var framework in project.GetProperty("frameworks").EnumerateArray())
foreach (var group in new[] { "topLevelPackages", "transitivePackages" })
{
    if (!framework.TryGetProperty(group, out var rows)) continue;
    foreach (var row in rows.EnumerateArray())
    {
        var id = row.GetProperty("id").GetString()!;
        var version = row.GetProperty("resolvedVersion").GetString()!;
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z") ||
            !Regex.IsMatch(version, @"\A[0-9][A-Za-z0-9.+-]*\z"))
            throw new InvalidDataException("Invalid package identity");
        packages[id.ToLowerInvariant() + "/" + version.ToLowerInvariant()] = (id, version);
    }
}
if (packages.Count == 0) throw new InvalidDataException("Empty package inventory");
// Validate all inputs before replacing the scoped generated output.
var archives = packages.Select(p => (p.Key, p.Value, Path: Path.Combine(cache, p.Key,
    p.Value.Id.ToLowerInvariant() + "." + p.Value.Version.ToLowerInvariant() + ".nupkg"))).ToArray();
foreach (var archive in archives)
{
    var current = new FileInfo(archive.Path) as FileSystemInfo;
    while (current != null)
    {
        if (current.LinkTarget != null) throw new InvalidDataException("Symlink cache input");
        current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
    }
    using var zip = ZipFile.OpenRead(archive.Path);
    foreach (var entry in zip.Entries) SafePath(entry.FullName);
}
if (Directory.Exists(output)) Directory.Delete(output, true);
Directory.CreateDirectory(output);
var manifest = new List<object>();
foreach (var archive in archives)
{
    using var zip = ZipFile.OpenRead(archive.Path);
    var nuspec = zip.Entries.Single(e => !e.FullName.Contains('/') && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
    using var stream = nuspec.Open();
    var xml = XDocument.Load(stream);
    var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    XElement? Field(string name) => metadata.Elements().SingleOrDefault(e => e.Name.LocalName == name);
    if (!string.Equals(Field("id")?.Value, archive.Value.Id, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(Field("version")?.Value, archive.Value.Version, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Nuspec identity mismatch");
    var license = Field("license");
    var licenseFile = license?.Attribute("type")?.Value == "file" ? license.Value : null;
    if (licenseFile != null) SafePath(licenseFile);
    var documents = new List<object>();
    foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
    {
        var path = entry.FullName;
        if (path.EndsWith('/')) continue;
        var name = Path.GetFileName(path);
        var isDocument = path == licenseFile || Regex.IsMatch(name,
            @"\A(?:licen[cs]e|notice|third[-_]party[-_]notices|copying|copyright)(?:[._-].*)?\z", RegexOptions.IgnoreCase);
        // Retain bundled attribution trees as shipped, including README/provenance.
        if (!isDocument && !path.Split('/').Any(p => p.Equals("third_party", StringComparison.OrdinalIgnoreCase))) continue;
        using var input = entry.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var content = bytes.ToArray();
        var destination = Path.Combine(output, "packages", archive.Key, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, content);
        documents.Add(new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), bytes = content.Length });
    }
    var metadataPath = Path.Combine(output, "packages", archive.Key, nuspec.FullName);
    Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
    using (var input = nuspec.Open())
    using (var target = File.Create(metadataPath)) input.CopyTo(target);
    manifest.Add(new {
        id = archive.Value.Id, version = archive.Value.Version,
        source = "restored-nupkg", packagePath = archive.Key + "/" + Path.GetFileName(archive.Path),
        nupkgSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive.Path))),
        nuspecPath = nuspec.FullName,
        licenseType = license?.Attribute("type")?.Value, license = license?.Value,
        licenseUrl = Field("licenseUrl")?.Value,
        repository = Field("repository")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
        status = documents.Count > 0 ? "documents-collected" : license != null || Field("licenseUrl") != null ? "metadata-only" : "missing",
        declaredLicenseFileMissing = licenseFile != null && !zip.Entries.Any(e => e.FullName == licenseFile),
        sourceRetrieval = "not-attempted", documents
    });
}
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest,
    new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Collected original package documents for {packages.Count} resolved packages; inspect manifest statuses and source-retrieval gaps.");

static void SafePath(string path)
{
    if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') ||
        path.Split('/').Any(p => p is ".." or ".")) throw new InvalidDataException("Unsafe archive path");
}
