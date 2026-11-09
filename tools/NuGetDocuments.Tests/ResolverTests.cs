using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;

public class ResolverTests
{
    [Fact]
    public async Task SourceResolutionPreservesSafetyCachingAndConcurrency()
    {
        using (var http = new HttpClient(new ApiHandler()))
        {
            var response = await GitHubClient.GetApi(http, "repos/owner/repo/git/ref/tags/1.2.3", "fixture-token");
            Check(response.Status == 200 && response.Json == "{}", "portable authenticated HTTP API");
        }
        Console.WriteLine("PASS GitHub API uses HTTP with scoped authentication and headers");
        foreach (var status in new[] { 404, 401, 403, 302, 503, 429 })
        {
            var handler = new StatusHandler(status);
            using var http = new HttpClient(handler);
            var response = await GitHubClient.GetApi(http, "repos/owner/repo/git/ref/tags/1.2.3");
            Check(response.Status == status && response.Json == null, "API failure status preserved without body");
            Check(handler.Calls == (status is 503 or 429 ? 3 : 1), "bounded transient retries only");
            Check(!handler.Authenticated, "optional token absent");
        }
        using (var http = new HttpClient(new StatusHandler(0)))
        {
            var response = await GitHubClient.GetApi(http, "repos/owner/repo/git/ref/tags/1.2.3");
            Check(response.Status == 0 && response.Json == null, "transport failure stays distinct from 404");
            response = await GitHubClient.GetApi(http, "https://evil.example/token", "fixture-token");
            Check(response.Status == 0, "reject non-API paths before sending credentials");
        }
        Console.WriteLine("PASS API absence/auth/redirect/transient/transport errors and optional token");

        const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string tagCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var treeCalls = 0;
        var rawCalls = 0;
        var treeResolver = new SourceResolver(async path =>
        {
            Interlocked.Increment(ref treeCalls);
            await Task.Delay(25);
            Check(path.EndsWith("/git/trees/" + commit), "one SHA-pinned root listing, no recursive tree");
            return ("{\"truncated\":false,\"tree\":[{\"path\":\"LICENSE\",\"type\":\"blob\",\"mode\":\"100644\"},{\"path\":\"NOTICE\",\"type\":\"blob\",\"mode\":\"100644\"}]}", 200);
        }, async url =>
        {
            Interlocked.Increment(ref rawCalls);
            await Task.Delay(25);
            return (Encoding.UTF8.GetBytes(url), 200, null);
        });
        var simultaneous = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => treeResolver.Resolve("owner/repo", "1.2.3", commit)));
        Check(treeCalls == 1 && rawCalls == 2, "root listing and downloads single-flight across concurrent packages");
        Check(simultaneous.All(r => r.Documents.Select(d => d.Name).SequenceEqual(new[] { "LICENSE", "NOTICE" })), "deterministic candidate order");
        Console.WriteLine("PASS root listing eliminates absent probes; concurrent single-flight and deterministic order");
        var active = 0;
        var peak = 0;
        var gateLock = new object();
        var bounded = new SourceResolver(async _ =>
        {
            lock (gateLock)
            {
                active++;
                peak = Math.Max(peak, active);
            }
            await Task.Delay(30);
            lock (gateLock)
            {
                active--;
            }

            return ("{\"truncated\":false,\"tree\":[]}", 200);
        }, _ => throw new Exception("empty tree must not download"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => bounded.Resolve("owner/repo" + i, "1.2.3", commit)));
        Check(peak > 1 && peak <= 4, "shared HTTP concurrency must be bounded at four");
        Console.WriteLine("PASS global HTTP concurrency bound across repositories");
        foreach (var status in new[] { 0, 401, 403, 429, 500 })
        {
            var calls = 0;
            var failed = new SourceResolver(_ => { calls++; return Task.FromResult<(string?, int)>((null, status)); },
                _ => throw new Exception("tree errors must not download"));
            var failedResults = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => failed.Resolve("owner/repo", "1.2.3", commit)));
            Check(calls == 1 && failedResults.All(r => r.Errors.Count == 1 && r.Tags.Count == 0), "tree failures cached and never trigger tag fallback");
        }
        foreach (var json in new[] { "{}", "{\"truncated\":true,\"tree\":[]}",
    "{\"truncated\":false,\"tree\":[{\"path\":\"LICENSE\",\"type\":\"blob\",\"mode\":\"120000\"}]}",
    "{\"truncated\":false,\"tree\":[{\"path\":\"LICENSE\",\"type\":\"commit\",\"mode\":\"160000\"}]}" })
        {
            var failed = new SourceResolver(_ => Task.FromResult<(string?, int)>((json, 200)), _ => throw new Exception("unsafe tree must not download"));
            var failure = await failed.Resolve("owner/repo", "1.2.3", commit);
            Check(failure.Errors.Count == 1 && failure.Tags.Count == 0, "malformed/truncated/symlink/submodule tree fails closed");
        }
        var absentRevision = new SourceResolver(path => Task.FromResult<(string?, int)>((null, 404)), _ => throw new Exception("absent revision has no documents"));
        var absentResult = await absentRevision.Resolve("owner/repo", "1.2.3", commit);
        Check(absentResult.FallbackReason == "nuspec-commit-license-absent" && absentResult.Error == "missing-version-tag", "404 revision retains exact-tag fallback");
        Console.WriteLine("PASS tree errors/auth/transport cached fail-closed; malformed/truncated/symlink/submodule rejected; 404 fallback");
        var apiCalls = new List<string>();
        var urls = new List<string>();
        var resolver = CreateResolver(
            path => { apiCalls.Add(path); throw new Exception("successful commit must not query tags"); },
            url => { urls.Add(url); return Task.FromResult<(byte[]?, int?, string?)>((url.EndsWith("/LICENSE") ? Encoding.UTF8.GetBytes("license") : null, url.EndsWith("/LICENSE") ? 200 : 404, null)); });
        var result = await resolver.Resolve("owner/repo", "1.2.3", commit);
        Check(result.Error == null && result.ResolvedSha == commit && result.Provenance == "nuspec-commit", "commit first");
        Check(apiCalls.Count == 0 && urls.All(u => u.Contains('/' + commit + '/')), "no tag lookup on successful commit");
        Console.WriteLine("PASS commit-first without tag lookup");

        apiCalls.Clear(); urls.Clear();
        var tagObject = "cccccccccccccccccccccccccccccccccccccccc";
        resolver = CreateResolver(path =>
        {
            apiCalls.Add(path);
            if (path.EndsWith("/git/ref/tags/1.2.3"))
            {
                return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/1.2.3\",\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}", 200));
            }

            if (path.EndsWith("/git/tags/" + tagObject))
            {
                return Task.FromResult<(string?, int)>(("{\"object\":{\"type\":\"commit\",\"sha\":\"" + tagCommit + "\"}}", 200));
            }

            return Task.FromResult<(string?, int)>((null, 404));
        }, url =>
        {
            urls.Add(url);
            var found = url.Contains('/' + tagCommit + '/') && url.EndsWith("/LICENSE");
            return Task.FromResult<(byte[]?, int?, string?)>((found ? Encoding.UTF8.GetBytes("tag license") : null, found ? 200 : 404, null));
        });
        result = await resolver.Resolve("owner/repo", "1.2.3", commit);
        Check(result.Error == null && result.ResolvedSha == tagCommit && result.Tag == "1.2.3", "annotated tag fallback");
        Check(result.Provenance == "exact-version-tag" && result.FallbackReason == "nuspec-commit-license-absent", "fallback provenance");
        Check(result.Documents.All(d => d.Revision == tagCommit) && urls.Any(u => u.Contains('/' + commit + '/')), "both pinned attempts");
        Check(apiCalls.Contains("repos/owner/repo/git/ref/tags/v1.2.3"), "both exact tags checked");
        Console.WriteLine("PASS absent commit license -> annotated tag -> pinned commit");

        // Missing commit uses the same cached tag metadata and document bytes.
        var beforeApi = apiCalls.Count;
        var beforeUrls = urls.Count;
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.ResolvedSha == tagCommit && result.FallbackReason == "missing-nuspec-commit", "missing commit fallback");
        Check(apiCalls.Count == beforeApi && urls.Count == beforeUrls, "shared repository/revision caches");
        Console.WriteLine("PASS missing commit + tag/download caching");

        resolver = CreateResolver(_ => Task.FromResult<(string?, int)>((null, 404)), _ => throw new Exception("no branch download"));
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.Error == "missing-version-tag" && result.ResolvedSha == null, "missing tags fail closed");
        Console.WriteLine("PASS missing tags have no branch fallback");

        resolver = CreateResolver(path =>
        {
            var tag = path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3";
            var sha = tag.StartsWith('v') ? commit : tagCommit;
            return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/" + tag + "\",\"object\":{\"type\":\"commit\",\"sha\":\"" + sha + "\"}}", 200));
        }, _ => throw new Exception("ambiguous tags must not download"));
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.Error == "ambiguous-version-tags" && result.ResolvedSha == null, "ambiguous tags fail closed");
        Console.WriteLine("PASS different version/vversion SHAs rejected");

        resolver = CreateResolver(_ => throw new Exception("transient errors must not query tags"),
            _ => Task.FromResult<(byte[]?, int?, string?)>((null, 503, "source-http-error")));
        result = await resolver.Resolve("owner/repo", "1.2.3", commit);
        Check(result.Errors.Count > 0 && result.Tags.Count == 0, "preserve transient commit errors");
        Console.WriteLine("PASS transient failures preserved without fallback");

        resolver = CreateResolver(path => Task.FromResult<(string?, int)>((null, path.EndsWith("/v1.2.3") ? 403 : 404)),
            _ => throw new Exception("API failure must not download"));
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.Error == "tag-resolution-error" && result.Tags.Any(t => t.Error == "tag-api-error"), "API error is not missing tag");
        Console.WriteLine("PASS tag API failures distinguished from absence");

        resolver = CreateResolver(_ => throw new Exception("no tags needed"), url =>
        {
            var found = url.EndsWith("/NOTICE.html") || url.EndsWith("/LICENSE");
            return Task.FromResult<(byte[]?, int?, string?)>((found ? Encoding.UTF8.GetBytes("original document") : null, found ? 200 : 404, null));
        });
        result = await resolver.Resolve("owner/repo", "1.2.3", commit);
        Check(result.Documents.Any(d => d.Name == "NOTICE.html"), "NOTICE.html candidate");
        Console.WriteLine("PASS NOTICE.html discovery");

        resolver = CreateResolver(path =>
        {
            var tag = path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3";
            return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/" + tag + "\",\"object\":{\"type\":\"commit\",\"sha\":\"" + tagCommit + "\"}}", 200));
        }, url => Task.FromResult<(byte[]?, int?, string?)>((url.EndsWith("/LICENSE") ? Encoding.UTF8.GetBytes("license") : null, url.EndsWith("/LICENSE") ? 200 : 404, null)));
        result = await resolver.Resolve("owner/repo", "1.2.3", "malformed");
        Check(result.Error == null && result.ResolvedSha == tagCommit && result.Tags.Count == 2 && result.FallbackReason == "invalid-nuspec-commit", "equivalent aliases allowed");
        Console.WriteLine("PASS same-SHA aliases + invalid nuspec commit fallback");

        resolver = CreateResolver(_ => Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/1.2.30\",\"object\":{\"type\":\"commit\",\"sha\":\"" + commit + "\"}}", 200)),
            _ => throw new Exception("non-exact ref must not download"));
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.Error == "tag-resolution-error" && result.Tags.All(t => t.Error == "non-exact-tag-ref"), "exact refs required");
        Console.WriteLine("PASS non-exact ref rejected");

        resolver = CreateResolver(path => Task.FromResult<(string?, int)>((path.Contains("/git/ref/")
            ? "{\"ref\":\"refs/tags/" + (path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3") + "\",\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}"
            : "{\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}", 200)),
            _ => throw new Exception("tag cycles must not download"));
        result = await resolver.Resolve("owner/repo", "1.2.3", null);
        Check(result.Error == "tag-resolution-error" && result.Tags.All(t => t.Error == "invalid-tag-object"), "tag cycle bounded");
        Console.WriteLine("PASS annotated tag cycle rejected");

        resolver = CreateResolver(_ => throw new Exception("title-case commit must not need tags"), url =>
        {
            var found = url.EndsWith("/License.txt");
            return Task.FromResult<(byte[]?, int?, string?)>((found ? Encoding.UTF8.GetBytes("original title-case license") : null, found ? 200 : 404, null));
        });
        result = await resolver.Resolve("dotnet/roslyn", "5.0.0", commit);
        Check(result.Documents.Any(d => d.Name == "License.txt") && result.Provenance == "nuspec-commit", "title-case root license");
        Console.WriteLine("PASS title-case License.txt at nuspec commit (Roslyn)");

        using (var http = new HttpClient(new HtmlHandler()))
        {
            var html = await GitHubClient.Download(http, "https://raw.githubusercontent.com/owner/repo/" + commit + "/NOTICE.html");
            Check(html.Bytes != null && html.Error == null, "original HTML notice accepted");
            html = await GitHubClient.Download(http, "https://raw.githubusercontent.com/owner/repo/" + commit + "/LICENSE");
            Check(html.Bytes == null && html.Error == "source-html-rejected", "HTML error pages still rejected");
        }
        Console.WriteLine("PASS original HTML NOTICE only; other HTML rejected");

        SourceResolver CreateResolver(Func<string, Task<(string? Json, int Status)>> api,
            Func<string, Task<(byte[]? Bytes, int? Status, string? Error)>> download) =>
            new SourceResolver(path => path.Contains("/git/trees/")
                ? Task.FromResult<(string?, int)>((JsonSerializer.Serialize(new { truncated = false, tree = new[] { "LICENSE", "License.txt", "NOTICE.html" }.Select(path => new { path, type = "blob", mode = "100644" }) }), 200))
                : api(path), download);

        void Check(bool value, string name)
        {
            if (!value)
            {
                throw new Exception(name);
            }
        }


    }
    class StatusHandler(int status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Authenticated { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authenticated |= request.Headers.Authorization != null;
            if (status == 0)
            {
                throw new HttpRequestException("fixture transport failure");
            }

            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent("diagnostics must not be retained") });
        }
    }

    class ApiHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.ToString() != "https://api.github.com/repos/owner/repo/git/ref/tags/1.2.3" ||
                request.Headers.Authorization?.ToString() != "Bearer fixture-token" ||
                !request.Headers.UserAgent.Any() || !request.Headers.Contains("X-GitHub-Api-Version"))
            {
                throw new Exception("API URI/auth/headers");
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    class HtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("<!DOCTYPE html><html>Original attribution</html>", Encoding.UTF8, "text/html") });
    }
}
