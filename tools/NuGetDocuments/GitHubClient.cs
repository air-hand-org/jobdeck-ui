using System.Text.RegularExpressions;

public static class GitHubClient
{
    public static async Task<(byte[]? Bytes, int? Status, string? Error)> Download(HttpClient http, string url)
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

    public static async Task<(string? Json, int Status)> GetApi(HttpClient http, string path, string? token = null)
    {
        // Fixed origin, no redirects: credentials must never reach raw document hosts.
        if (!Regex.IsMatch(path, @"\Arepos/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/git/(?:ref/tags/[^/?#]+|(?:tags|trees)/[0-9a-fA-F]{40})\z"))
        {
            return (null, 0);
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/" + path);
                request.Headers.UserAgent.ParseAdd(".NET HttpClient");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new("Bearer", token);
                }

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
                if (attempt == 2)
                {
                    return (null, 0);
                }

                await Task.Delay(250 * (attempt + 1));
            }
        }
        throw new InvalidOperationException("Unreachable API state");
    }
}
