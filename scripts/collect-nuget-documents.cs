#:property PublishAot=false
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Preserve original documents from restored nupkg files, never SPDX templates.
var inventory = JsonDocument.Parse(File.ReadAllText(args[0]));
var cache = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var packages = new SortedDictionary<string, (string Id, string Version)>(StringComparer.Ordinal);
foreach (var project in inventory.RootElement.GetProperty("projects").EnumerateArray())
{
    foreach (var framework in project.GetProperty("frameworks").EnumerateArray())
    {
        foreach (var group in new[] { "topLevelPackages", "transitivePackages" })
        {
            if (!framework.TryGetProperty(group, out var rows))
            {
                continue;
            }

            foreach (var row in rows.EnumerateArray())
            {
                var id = row.GetProperty("id").GetString()!;
                var version = row.GetProperty("resolvedVersion").GetString()!;
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
var archives = packages.Select(p => (p.Key, p.Value, Path: Path.Combine(cache, p.Key,
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
        SafePath(entry.FullName);
    }
}
if (Directory.Exists(output))
{
    Directory.Delete(output, true);
}

Directory.CreateDirectory(output);
var manifest = new List<object>();
var unresolved = 0;
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
var downloads = new Dictionary<string, (byte[]? Bytes, int? Status, string? Error)>();
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
    {
        throw new InvalidDataException("Nuspec identity mismatch");
    }

    var license = Field("license");
    var licenseFile = license?.Attribute("type")?.Value == "file" ? license.Value : null;
    if (licenseFile != null)
    {
        SafePath(licenseFile);
    }

    var documents = new List<object>();
    foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
    {
        var path = entry.FullName;
        if (path.EndsWith('/'))
        {
            continue;
        }

        var name = Path.GetFileName(path);
        var isDocument = path == licenseFile || Regex.IsMatch(name,
            @"\A(?:licen[cs]e|notice|third[-_]party[-_]notices|copying|copyright)(?:[._-].*)?\z", RegexOptions.IgnoreCase);
        // Retain bundled attribution trees as shipped, including README/provenance.
        if (!isDocument && !path.Split('/').Any(p => p.Equals("third_party", StringComparison.OrdinalIgnoreCase)))
        {
            continue;
        }

        using var input = entry.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        var content = bytes.ToArray();
        var destination = Path.Combine(output, "packages", archive.Key, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, content);
        documents.Add(new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), bytes = content.Length });
    }
    var errors = new List<object>();
    var sourceDocuments = new List<object>();
    var sourceAttempts = new List<object>();
    var hasLicense = zip.Entries.Any(e => !e.FullName.EndsWith('/') &&
        (e.FullName == licenseFile || IsLicense(Path.GetFileName(e.FullName))));
    var sourceRetrieval = "not-needed-package-license-present";
    if (!hasLicense)
    {
        sourceRetrieval = "exact-commit-root-candidates";
        var repository = Field("repository");
        var url = repository?.Attribute("url")?.Value;
        var revision = repository?.Attribute("commit")?.Value;
        var match = Regex.Match(url ?? "", @"\Ahttps://github\.com/([A-Za-z0-9_-][A-Za-z0-9_.-]*)/([A-Za-z0-9_-][A-Za-z0-9_.-]*?)(?:\.git)?/?\z");
        if (string.IsNullOrEmpty(url))
        {
            errors.Add(new { code = "missing-repository" });
        }
        else if (!match.Success)
        {
            errors.Add(new { code = "unsafe-or-unsupported-repository" });
        }
        else if (string.IsNullOrEmpty(revision))
        {
            errors.Add(new { code = "missing-commit" });
        }
        else if (!Regex.IsMatch(revision, @"\A[0-9a-fA-F]{40}\z"))
        {
            errors.Add(new { code = "invalid-exact-commit" });
        }
        else
        {
            foreach (var stem in new[] { "LICENSE", "LICENCE", "COPYING", "NOTICE", "THIRD-PARTY-NOTICES" })
            {
                foreach (var name in new[] { stem, stem + ".txt", stem + ".md", stem + ".TXT", stem.ToLowerInvariant(), stem.ToLowerInvariant() + ".txt", stem.ToLowerInvariant() + ".md" })
                {
                    var sourceUrl = $"https://raw.githubusercontent.com/{match.Groups[1].Value}/{match.Groups[2].Value}/{revision}/{name}";
                    if (!downloads.TryGetValue(sourceUrl, out var result))
                    {
                        result = await Download(http, sourceUrl);
                        downloads.Add(sourceUrl, result);
                    }
                    sourceAttempts.Add(new
                    {
                        url = sourceUrl,
                        revision,
                        httpStatus = result.Status,
                        status = result.Status == 404 ? "document-absent" : result.Error ?? "collected"
                    });
                    if (result.Error != null)
                    {
                        errors.Add(new { code = result.Error, url = sourceUrl, httpStatus = result.Status });
                    }

                    if (result.Bytes == null)
                    {
                        continue;
                    }

                    var destination = Path.Combine(output, "source", archive.Key, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, result.Bytes);
                    sourceDocuments.Add(new
                    {
                        path = name,
                        url = sourceUrl,
                        revision,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(result.Bytes)),
                        bytes = result.Bytes.Length
                    });
                    if (IsLicense(name))
                    {
                        hasLicense = true;
                    }
                }
            }
        }
    }
    if (!hasLicense)
    {
        errors.Add(new { code = "unresolved-original-license" });
    }

    var declaredLicenseFileMissing = licenseFile != null && !zip.Entries.Any(e => e.FullName == licenseFile);
    if (declaredLicenseFileMissing)
    {
        errors.Add(new { code = "declared-license-file-missing" });
    }

    if (errors.Count > 0)
    {
        unresolved++;
    }

    manifest.Add(new
    {
        id = archive.Value.Id,
        version = archive.Value.Version,
        source = "restored-nupkg",
        packagePath = archive.Key + "/" + Path.GetFileName(archive.Path),
        nupkgSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive.Path))),
        nuspecPath = nuspec.FullName,
        licenseType = license?.Attribute("type")?.Value,
        license = license?.Value,
        licenseUrl = Field("licenseUrl")?.Value,
        repository = Field("repository")?.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
        status = errors.Count > 0 ? "error" : "original-license-collected",
        declaredLicenseFileMissing,
        sourceRetrieval,
        documents,
        sourceDocuments,
        sourceAttempts,
        errors,
        attributionScope = "Bundled documents first; fallback checks standard repository-root names only. A collected LICENSE does not establish complete attribution. Packaged licenses may omit source NOTICE files; those are not probed when a package license is present."
    });
}
File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest,
    new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Collected {packages.Count} packages; {unresolved} packages have unresolved collection errors (manifest.json).");
Environment.ExitCode = unresolved == 0 ? 0 : 2;

static bool IsLicense(string name) => Regex.IsMatch(name, @"\A(?:licen[cs]e|copying)(?:[._-].*)?\z", RegexOptions.IgnoreCase);

static async Task<(byte[]? Bytes, int? Status, string? Error)> Download(HttpClient http, string url)
{
    const int limit = 1024 * 1024;
    for (var attempt = 0; attempt < 3; attempt++)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (status == 404)
            {
                return (null, status, null);
            }

            if ((status == 429 || status >= 500) && attempt < 2)
            {
                await Task.Delay(250 * (attempt + 1));
                continue;
            }
            if (status != 200)
            {
                return (null, status, status is 401 or 403 ? "source-auth-error" : "source-http-error");
            }

            if (response.Content.Headers.ContentLength > limit)
            {
                return (null, status, "source-too-large");
            }

            var media = response.Content.Headers.ContentType?.MediaType;
            if (media is "text/html" or "application/xhtml+xml")
            {
                return (null, status, "source-html-rejected");
            }

            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (bytes.Length + read > limit)
                {
                    return (null, status, "source-too-large");
                }

                bytes.Write(buffer, 0, read);
            }
            var content = bytes.ToArray();
            if (content.Length == 0)
            {
                return (null, status, "source-empty-document");
            }

            var prefix = System.Text.Encoding.UTF8.GetString(content.AsSpan(0, Math.Min(512, content.Length))).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (prefix.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
            {
                return (null, status, "source-html-rejected");
            }

            return (content, status, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            if (attempt == 2)
            {
                return (null, null, ex is OperationCanceledException ? "source-timeout" : "source-transport-error");
            }

            await Task.Delay(250 * (attempt + 1));
        }
    }
    throw new InvalidOperationException("Unreachable download state");
}

static void SafePath(string path)
{
    if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') ||
        path.Split('/').Any(p => p is ".." or "."))
    {
        throw new InvalidDataException("Unsafe archive path");
    }
}
