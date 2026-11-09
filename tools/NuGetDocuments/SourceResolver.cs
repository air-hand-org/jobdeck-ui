using System.Text.Json;
using System.Text.RegularExpressions;

public sealed class SourceResolver
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

    private readonly SemaphoreSlim requests = new(4);
    private readonly Func<string, Task<(string? Json, int Status)>> api;
    private readonly Func<string, Task<(byte[]? Bytes, int? Status, string? Error)>> download;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<(byte[]? Bytes, int? Status, string? Error)>>> downloads = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<(string? Json, int Status)>>> apiCache = new();

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
            if (result.Errors.Count > 0 || result.Documents.Any(d => IsLicense(d.Name)))
            {
                return result;
            }

            result.FallbackReason = "nuspec-commit-license-absent";
        }
        else
        {
            result.FallbackReason = string.IsNullOrEmpty(commit) ? "missing-nuspec-commit" : "invalid-nuspec-commit";
        }
        // Neither an arbitrary branch nor the unverified nuspec revision is a tag fallback.
        result.ResolvedSha = null;
        result.Provenance = null;
        foreach (var tag in new[] { version, "v" + version })
        {
            result.Tags.Add(await ResolveTag(repository, tag));
        }

        if (result.Tags.Any(t => t.Error != null && t.Error != "tag-absent"))
        {
            result.Error = "tag-resolution-error";
            return result;
        }
        var found = result.Tags.Where(t => t.Sha != null).ToArray();
        if (found.Length == 0)
        {
            result.Error = "missing-version-tag";
        }
        else if (found.Select(t => t.Sha).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            result.Error = "ambiguous-version-tags";
        }
        else
        {
            result.Tag = found[0].Tag;
            result.ResolvedSha = found[0].Sha;
            result.Provenance = "exact-version-tag";
            await Collect(repository, result.ResolvedSha!, result);
        }
        return result;
    }

    private static bool IsLicense(string name) => PackageReader.IsLicense(name);

    private async Task Collect(string repository, string sha, Result result)
    {
        var listingPath = $"repos/{repository}/git/trees/{sha}";
        var listing = await GetApi(listingPath);
        result.Attempts.Add(new
        {
            apiPath = listingPath,
            revision = sha,
            httpStatus = listing.Status,
            status = listing.Status == 404 ? "revision-absent" : listing.Status == 200 ? "root-listed" : "source-tree-api-error"
        });
        if (listing.Status == 404)
        {
            return;
        }

        if (listing.Status != 200)
        {
            result.Errors.Add(new { code = "source-tree-api-error", apiPath = listingPath, httpStatus = listing.Status });
            return;
        }
        HashSet<string> files = new(StringComparer.Ordinal);
        try
        {
            using var tree = JsonDocument.Parse(listing.Json!);
            if (tree.RootElement.GetProperty("truncated").GetBoolean())
            {
                throw new InvalidDataException("Truncated root tree");
            }

            foreach (var entry in tree.RootElement.GetProperty("tree").EnumerateArray())
            {
                var name = entry.GetProperty("path").GetString()!;
                // Never follow symlinks, submodules or nested paths from untrusted listings.
                if (!RootNames().Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                if (entry.GetProperty("type").GetString() != "blob" ||
                    entry.GetProperty("mode").GetString() is not ("100644" or "100755") || !files.Add(name))
                {
                    throw new InvalidDataException("Unsafe or duplicate root document");
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            result.Errors.Add(new { code = "invalid-source-tree", apiPath = listingPath });
            return;
        }
        foreach (var name in RootNames().Where(files.Contains))
        {
            var url = $"https://raw.githubusercontent.com/{repository}/{sha}/{name}";
            var response = await downloads.GetOrAdd(url, key => new(() => Limited(() => download(key)))).Value;
            result.Attempts.Add(new
            {
                url,
                revision = sha,
                httpStatus = response.Status,
                status = response.Status == 404 ? "document-absent" : response.Error ?? "collected"
            });
            if (response.Error != null)
            {
                result.Errors.Add(new { code = response.Error, url, httpStatus = response.Status });
            }

            if (response.Bytes != null)
            {
                result.Documents.Add(new Document(name, url, sha, response.Bytes));
            }
        }
    }

    private async Task<T> Limited<T>(Func<Task<T>> action)
    {
        await requests.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            requests.Release();
        }
    }

    private Task<(string? Json, int Status)> GetApi(string path) =>
        apiCache.GetOrAdd(path, key => new(() => Limited(() => api(key)))).Value;

    private static IEnumerable<string> RootNames() =>
        new[] { "LICENSE", "LICENCE", "COPYING", "NOTICE", "THIRD-PARTY-NOTICES" }.SelectMany(stem =>
            new[] { stem, stem + ".txt", stem + ".md", stem + ".TXT", stem.ToLowerInvariant(), stem.ToLowerInvariant() + ".txt", stem.ToLowerInvariant() + ".md",
                stem[..1] + stem[1..].ToLowerInvariant(), stem[..1] + stem[1..].ToLowerInvariant() + ".txt", stem[..1] + stem[1..].ToLowerInvariant() + ".md" }
            .Concat(stem == "NOTICE" ? new[] { "NOTICE.html", "notice.html" } : Array.Empty<string>()));

    private async Task<Candidate> ResolveTag(string repository, string tag)
    {
        try
        {
            var response = await GetApi($"repos/{repository}/git/ref/tags/{Uri.EscapeDataString(tag)}");
            if (response.Status == 404)
            {
                return new(tag, null, "tag-absent");
            }

            if (response.Status != 200)
            {
                return new(tag, null, "tag-api-error");
            }

            using var reference = JsonDocument.Parse(response.Json!);
            if (reference.RootElement.GetProperty("ref").GetString() != "refs/tags/" + tag)
            {
                return new(tag, null, "non-exact-tag-ref");
            }

            var obj = reference.RootElement.GetProperty("object").Clone();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var depth = 0; depth < 16; depth++)
            {
                var sha = obj.GetProperty("sha").GetString();
                var type = obj.GetProperty("type").GetString();
                if (!Regex.IsMatch(sha ?? "", @"\A[0-9a-fA-F]{40}\z"))
                {
                    return new(tag, null, "invalid-tag-sha");
                }

                if (type == "commit")
                {
                    return new(tag, sha!.ToLowerInvariant(), null);
                }

                if (type != "tag" || !visited.Add(sha!))
                {
                    return new(tag, null, "invalid-tag-object");
                }

                response = await GetApi($"repos/{repository}/git/tags/{sha}");
                if (response.Status != 200)
                {
                    return new(tag, null, "tag-object-api-error");
                }

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

}
