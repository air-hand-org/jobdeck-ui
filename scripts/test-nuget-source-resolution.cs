#:property PublishAot=false
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;

const string commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const string tagCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
var apiCalls = new List<string>();
var urls = new List<string>();
var resolver = new SourceResolver(
    path => { apiCalls.Add(path); throw new Exception("successful commit must not query tags"); },
    url => { urls.Add(url); return Task.FromResult<(byte[]?, int?, string?)>((url.EndsWith("/LICENSE") ? Encoding.UTF8.GetBytes("license") : null, url.EndsWith("/LICENSE") ? 200 : 404, null)); });
var result = await resolver.Resolve("owner/repo", "1.2.3", commit);
Check(result.Error == null && result.ResolvedSha == commit && result.Provenance == "nuspec-commit", "commit first");
Check(apiCalls.Count == 0 && urls.All(u => u.Contains('/' + commit + '/')), "no tag lookup on successful commit");
Console.WriteLine("PASS commit-first without tag lookup");

apiCalls.Clear(); urls.Clear();
var tagObject = "cccccccccccccccccccccccccccccccccccccccc";
resolver = new SourceResolver(path =>
{
    apiCalls.Add(path);
    if (path.EndsWith("/git/ref/tags/1.2.3")) return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/1.2.3\",\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}", 200));
    if (path.EndsWith("/git/tags/" + tagObject)) return Task.FromResult<(string?, int)>(("{\"object\":{\"type\":\"commit\",\"sha\":\"" + tagCommit + "\"}}", 200));
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

resolver = new SourceResolver(_ => Task.FromResult<(string?, int)>((null, 404)), _ => throw new Exception("no branch download"));
result = await resolver.Resolve("owner/repo", "1.2.3", null);
Check(result.Error == "missing-version-tag" && result.ResolvedSha == null, "missing tags fail closed");
Console.WriteLine("PASS missing tags have no branch fallback");

resolver = new SourceResolver(path =>
{
    var tag = path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3";
    var sha = tag.StartsWith('v') ? commit : tagCommit;
    return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/" + tag + "\",\"object\":{\"type\":\"commit\",\"sha\":\"" + sha + "\"}}", 200));
}, _ => throw new Exception("ambiguous tags must not download"));
result = await resolver.Resolve("owner/repo", "1.2.3", null);
Check(result.Error == "ambiguous-version-tags" && result.ResolvedSha == null, "ambiguous tags fail closed");
Console.WriteLine("PASS different version/vversion SHAs rejected");

resolver = new SourceResolver(_ => throw new Exception("transient errors must not query tags"),
    _ => Task.FromResult<(byte[]?, int?, string?)>((null, 503, "source-http-error")));
result = await resolver.Resolve("owner/repo", "1.2.3", commit);
Check(result.Errors.Count > 0 && result.Tags.Count == 0, "preserve transient commit errors");
Console.WriteLine("PASS transient failures preserved without fallback");

resolver = new SourceResolver(path => Task.FromResult<(string?, int)>((null, path.EndsWith("/v1.2.3") ? 403 : 404)),
    _ => throw new Exception("API failure must not download"));
result = await resolver.Resolve("owner/repo", "1.2.3", null);
Check(result.Error == "tag-resolution-error" && result.Tags.Any(t => t.Error == "tag-api-error"), "API error is not missing tag");
Console.WriteLine("PASS tag API failures distinguished from absence");

resolver = new SourceResolver(_ => throw new Exception("no tags needed"), url =>
{
    var found = url.EndsWith("/NOTICE.html") || url.EndsWith("/LICENSE");
    return Task.FromResult<(byte[]?, int?, string?)>((found ? Encoding.UTF8.GetBytes("original document") : null, found ? 200 : 404, null));
});
result = await resolver.Resolve("owner/repo", "1.2.3", commit);
Check(result.Documents.Any(d => d.Name == "NOTICE.html"), "NOTICE.html candidate");
Console.WriteLine("PASS NOTICE.html discovery");

resolver = new SourceResolver(path =>
{
    var tag = path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3";
    return Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/" + tag + "\",\"object\":{\"type\":\"commit\",\"sha\":\"" + tagCommit + "\"}}", 200));
}, url => Task.FromResult<(byte[]?, int?, string?)>((url.EndsWith("/LICENSE") ? Encoding.UTF8.GetBytes("license") : null, url.EndsWith("/LICENSE") ? 200 : 404, null)));
result = await resolver.Resolve("owner/repo", "1.2.3", "malformed");
Check(result.Error == null && result.ResolvedSha == tagCommit && result.Tags.Count == 2 && result.FallbackReason == "invalid-nuspec-commit", "equivalent aliases allowed");
Console.WriteLine("PASS same-SHA aliases + invalid nuspec commit fallback");

resolver = new SourceResolver(_ => Task.FromResult<(string?, int)>(("{\"ref\":\"refs/tags/1.2.30\",\"object\":{\"type\":\"commit\",\"sha\":\"" + commit + "\"}}", 200)),
    _ => throw new Exception("non-exact ref must not download"));
result = await resolver.Resolve("owner/repo", "1.2.3", null);
Check(result.Error == "tag-resolution-error" && result.Tags.All(t => t.Error == "non-exact-tag-ref"), "exact refs required");
Console.WriteLine("PASS non-exact ref rejected");

resolver = new SourceResolver(path => Task.FromResult<(string?, int)>((path.Contains("/git/ref/")
    ? "{\"ref\":\"refs/tags/" + (path.EndsWith("/v1.2.3") ? "v1.2.3" : "1.2.3") + "\",\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}"
    : "{\"object\":{\"type\":\"tag\",\"sha\":\"" + tagObject + "\"}}", 200)),
    _ => throw new Exception("tag cycles must not download"));
result = await resolver.Resolve("owner/repo", "1.2.3", null);
Check(result.Error == "tag-resolution-error" && result.Tags.All(t => t.Error == "invalid-tag-object"), "tag cycle bounded");
Console.WriteLine("PASS annotated tag cycle rejected");

using (var http = new HttpClient(new HtmlHandler()))
{
    var html = await Download(http, "https://raw.githubusercontent.com/owner/repo/" + commit + "/NOTICE.html");
    Check(html.Bytes != null && html.Error == null, "original HTML notice accepted");
    html = await Download(http, "https://raw.githubusercontent.com/owner/repo/" + commit + "/LICENSE");
    Check(html.Bytes == null && html.Error == "source-html-rejected", "HTML error pages still rejected");
}
Console.WriteLine("PASS original HTML NOTICE only; other HTML rejected");

void Check(bool value, string name) { if (!value) throw new Exception(name); }
// DOWNLOAD_FUNCTION

class HtmlHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StringContent("<!DOCTYPE html><html>Original attribution</html>", Encoding.UTF8, "text/html") });
}
