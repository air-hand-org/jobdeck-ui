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
var resolver = new SourceResolver(path => SourceResolver.GitHubApi(http, path,
    Environment.GetEnvironmentVariable("GITHUB_TOKEN")), url => Download(http, url));
using (var progress = new ConsoleProgress())
{
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
        SourceResolver.Result? resolution = null;
        var sourceRetrieval = "not-needed-package-license-present";
        if (!hasLicense)
        {
            sourceRetrieval = "nuspec-commit-then-exact-version-tags";
            var repository = Field("repository");
            var url = repository?.Attribute("url")?.Value;
            var match = Regex.Match(url ?? "", @"\Ahttps://github\.com/([A-Za-z0-9_-][A-Za-z0-9_.-]*)/([A-Za-z0-9_-][A-Za-z0-9_.-]*?)(?:\.git)?/?\z");
            if (!match.Success)
            {
                errors.Add(new { code = string.IsNullOrEmpty(url) ? "missing-repository" : "unsafe-or-unsupported-repository" });
            }
            else
            {
                resolution = await resolver.Resolve($"{match.Groups[1].Value}/{match.Groups[2].Value}", archive.Value.Version, repository?.Attribute("commit")?.Value);
                sourceAttempts.AddRange(resolution.Attempts);
                errors.AddRange(resolution.Errors);
                if (resolution.Error != null)
                    errors.Add(new { code = resolution.Error });
                foreach (var document in resolution.Documents)
                {
                    var path = document.Revision + "/" + document.Name;
                    var destination = Path.Combine(output, "source", archive.Key, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, document.Bytes);
                    sourceDocuments.Add(new
                    {
                        path,
                        url = document.Url,
                        revision = document.Revision,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(document.Bytes)),
                        bytes = document.Bytes.Length
                    });
                    if (IsLicense(document.Name)) hasLicense = true;
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
            sourceResolution = resolution == null ? null : new
            {
                nuspecCommit = Field("repository")?.Attribute("commit")?.Value,
                tag = resolution.Tag,
                resolvedSha = resolution.ResolvedSha,
                provenance = resolution.Provenance,
                fallbackReason = resolution.FallbackReason,
                nuspecCommitMismatch = resolution.ResolvedSha == null || string.IsNullOrEmpty(Field("repository")?.Attribute("commit")?.Value)
                    ? (bool?)null : !string.Equals(resolution.ResolvedSha, Field("repository")?.Attribute("commit")?.Value, StringComparison.OrdinalIgnoreCase),
                tagCandidates = resolution.Tags.Select(t => new { tag = t.Tag, resolvedSha = t.Sha, error = t.Error })
            },
            documents,
            sourceDocuments,
            sourceAttempts,
            errors,
            attributionScope = "Bundled documents first; fallback checks standard repository-root names only. A collected LICENSE does not establish complete attribution. Packaged licenses may omit source NOTICE files; those are not probed when a package license is present."
        });
        progress.Write(archives.IndexOf(archive), archives.Length);
    }
}

File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest,
    new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"Collected {packages.Count} packages; {unresolved} packages have unresolved collection errors (manifest.json).");
if (unresolved != 0)
{
    var errorPackages = manifest.Select(x => (dynamic)x).Where(x => x.status == "error").Select(x => $"{x.id}: v{x.version}");
    Console.Write($"Unresolved packages below:\n{string.Join("\n", errorPackages)}\n");
}
Environment.ExitCode = unresolved == 0 ? 0 : 2;

static bool IsLicense(string name) => Regex.IsMatch(name, @"\A(?:licen[cs]e|copying)(?:[._-].*)?\z", RegexOptions.IgnoreCase);

static async Task<(byte[]? Bytes, int? Status, string? Error)> Download(HttpClient http, string url)
{
    const int limit = 1024 * 1024;
    // HTML is a legitimate original NOTICE format, but only on our pinned raw URLs.
    var htmlNotice = Regex.IsMatch(url, @"\Ahttps://raw\.githubusercontent\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/[0-9a-fA-F]{40}/(?:NOTICE|notice)\.html\z");
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
            if (!htmlNotice && media is ("text/html" or "application/xhtml+xml"))
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
            if (!htmlNotice && (prefix.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase)))
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

class ConsoleProgress : IDisposable
{
    private int _previousLength;

    public ConsoleProgress()
    {
        Clear();
    }

    public void Dispose()
    {
        Clear();
    }

    private void Clear()
    {
        Console.WriteLine();
        _previousLength = 0;
    }

    public void Write(int current, int total)
    {
        string message = $"Progress: {current}/{total} ({current * 100 / total}%)";
        Console.Write($"\r{message.PadRight(_previousLength)}");
        _previousLength = message.Length;
    }
}

class SourceResolver
{
    public record Document(string Name, string Url, string Revision, byte[] Bytes);
    public record Candidate(string Tag, string? Sha, string? Error);
    public class Result
    {
        public string? Error { get; set; }
        public string? Tag { get; set; }
        public string? ResolvedSha { get; set; }
        public string? Provenance { get; set; }
        public string? FallbackReason { get; set; }
        public List<Candidate> Tags { get; } = [];
        public List<Document> Documents { get; } = [];
        public List<object> Attempts { get; } = [];
        public List<object> Errors { get; } = [];
    }

    private readonly Func<string, Task<(string? Json, int Status)>> api;
    private readonly Func<string, Task<(byte[]? Bytes, int? Status, string? Error)>> download;
    private readonly Dictionary<string, (byte[]? Bytes, int? Status, string? Error)> downloads = new();
    private readonly Dictionary<string, (string? Json, int Status)> apiCache = new();

    public SourceResolver(Func<string, Task<(string? Json, int Status)>> api,
        Func<string, Task<(byte[]? Bytes, int? Status, string? Error)>> download)
    {
        this.api = api;
        this.download = download;
    }

    public async Task<Result> Resolve(string repository, string version, string? commit)
    {
        var result = new Result();
        if (Regex.IsMatch(commit ?? "", @"\A[0-9a-fA-F]{40}\z"))
        {
            result.ResolvedSha = commit;
            result.Provenance = "nuspec-commit";
            await Collect(repository, commit!, result);
            if (result.Errors.Count > 0 || result.Documents.Any(d => IsLicense(d.Name))) return result;
            result.FallbackReason = "nuspec-commit-license-absent";
        }
        else result.FallbackReason = string.IsNullOrEmpty(commit) ? "missing-nuspec-commit" : "invalid-nuspec-commit";
        // Neither an arbitrary branch nor the unverified nuspec revision is a tag fallback.
        result.ResolvedSha = null;
        result.Provenance = null;
        foreach (var tag in new[] { version, "v" + version })
            result.Tags.Add(await ResolveTag(repository, tag));
        if (result.Tags.Any(t => t.Error != null && t.Error != "tag-absent"))
        {
            result.Error = "tag-resolution-error";
            return result;
        }
        var found = result.Tags.Where(t => t.Sha != null).ToArray();
        if (found.Length == 0) result.Error = "missing-version-tag";
        else if (found.Select(t => t.Sha).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            result.Error = "ambiguous-version-tags";
        else
        {
            result.Tag = found[0].Tag;
            result.ResolvedSha = found[0].Sha;
            result.Provenance = "exact-version-tag";
            await Collect(repository, result.ResolvedSha!, result);
        }
        return result;
    }

    private static bool IsLicense(string name) => Regex.IsMatch(name, @"\A(?:licen[cs]e|copying)(?:[._-].*)?\z", RegexOptions.IgnoreCase);

    private async Task Collect(string repository, string sha, Result result)
    {
        foreach (var stem in new[] { "LICENSE", "LICENCE", "COPYING", "NOTICE", "THIRD-PARTY-NOTICES" })
        foreach (var name in new[] { stem, stem + ".txt", stem + ".md", stem + ".TXT", stem.ToLowerInvariant(), stem.ToLowerInvariant() + ".txt", stem.ToLowerInvariant() + ".md" }
            .Concat(new[] { stem[..1] + stem[1..].ToLowerInvariant(), stem[..1] + stem[1..].ToLowerInvariant() + ".txt", stem[..1] + stem[1..].ToLowerInvariant() + ".md" })
            .Concat(stem == "NOTICE" ? new[] { "NOTICE.html", "notice.html" } : Array.Empty<string>()))
        {
            var url = $"https://raw.githubusercontent.com/{repository}/{sha}/{name}";
            if (!downloads.TryGetValue(url, out var response))
            {
                response = await download(url);
                downloads.Add(url, response);
            }
            result.Attempts.Add(new { url, revision = sha, httpStatus = response.Status,
                status = response.Status == 404 ? "document-absent" : response.Error ?? "collected" });
            if (response.Error != null) result.Errors.Add(new { code = response.Error, url, httpStatus = response.Status });
            if (response.Bytes != null) result.Documents.Add(new Document(name, url, sha, response.Bytes));
        }
    }

    private async Task<Candidate> ResolveTag(string repository, string tag)
    {
        async Task<(string? Json, int Status)> Get(string path)
        {
            if (!apiCache.TryGetValue(path, out var response))
            {
                response = await api(path);
                apiCache.Add(path, response);
            }
            return response;
        }
        try
        {
            var response = await Get($"repos/{repository}/git/ref/tags/{Uri.EscapeDataString(tag)}");
            if (response.Status == 404) return new(tag, null, "tag-absent");
            if (response.Status != 200) return new(tag, null, "tag-api-error");
            using var reference = JsonDocument.Parse(response.Json!);
            if (reference.RootElement.GetProperty("ref").GetString() != "refs/tags/" + tag)
                return new(tag, null, "non-exact-tag-ref");
            var obj = reference.RootElement.GetProperty("object").Clone();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var depth = 0; depth < 16; depth++)
            {
                var sha = obj.GetProperty("sha").GetString();
                var type = obj.GetProperty("type").GetString();
                if (!Regex.IsMatch(sha ?? "", @"\A[0-9a-fA-F]{40}\z")) return new(tag, null, "invalid-tag-sha");
                if (type == "commit") return new(tag, sha!.ToLowerInvariant(), null);
                if (type != "tag" || !visited.Add(sha!)) return new(tag, null, "invalid-tag-object");
                response = await Get($"repos/{repository}/git/tags/{sha}");
                if (response.Status != 200) return new(tag, null, "tag-object-api-error");
                using var annotated = JsonDocument.Parse(response.Json!);
                obj = annotated.RootElement.GetProperty("object").Clone();
            }
            return new(tag, null, "tag-depth-exceeded");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return new(tag, null, "invalid-tag-response");
        }
    }

    public static async Task<(string? Json, int Status)> GitHubApi(HttpClient http, string path, string? token = null)
    {
        // Fixed origin, no redirects: credentials must never reach raw document hosts.
        if (!Regex.IsMatch(path, @"\Arepos/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/git/(?:ref/tags/[^/?#]+|tags/[0-9a-fA-F]{40})\z"))
            return (null, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
                request.Headers.UserAgent.ParseAdd("jobdeck-nuget-documents/1.0");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new("Bearer", token);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = await http.SendAsync(request, timeout.Token);
                var status = (int)response.StatusCode;
                if ((status == 429 || status >= 500) && attempt < 2)
                {
                    await Task.Delay(250 * (attempt + 1));
                    continue;
                }
                // Error bodies can contain diagnostics; only retain successful JSON.
                return (status == 200 ? await response.Content.ReadAsStringAsync(timeout.Token) : null, status);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
            {
                if (attempt == 2) return (null, 0);
                await Task.Delay(250 * (attempt + 1));
            }
        }
        throw new InvalidOperationException("Unreachable API state");
    }
}
