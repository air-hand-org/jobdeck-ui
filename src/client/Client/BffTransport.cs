using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ui.Client;

// Only same-origin cookies and an antiforgery token cross this boundary.
// Never retry mutations: a lost response does not mean the operation failed.
public sealed class BffTransport(HttpClient http)
{
    public async Task<ApiResult<string>> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (method != HttpMethod.Get)
            {
                using var csrf = await http.GetAsync("bff/antiforgery", cancellationToken);
                if (!csrf.IsSuccessStatusCode)
                {
                    return Failure(csrf.StatusCode);
                }
                var token = await csrf.Content.ReadFromJsonAsync<AntiforgeryToken>(cancellationToken);
                if (string.IsNullOrWhiteSpace(token?.Token))
                {
                    return ApiResult<string>.Failure(new(ApiErrorCode.Internal, "操作を開始できませんでした。再読み込みしてください。"));
                }
                request.Headers.Add("X-CSRF-TOKEN", token.Token);
                request.Content = JsonContent.Create(body);
            }
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(response.StatusCode);
            }
            return await response.Content.ReadFromJsonAsync<ApiResult<string>>(cancellationToken)
                ?? ApiResult<string>.Failure(new(ApiErrorCode.Internal, "応答を読み取れませんでした。再読み込みしてください。"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return ApiResult<string>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。操作の結果は一覧で確認してください。"));
        }
    }

    public async Task<ApiResult<T>> SendDataAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(method, path, body, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiResult<T>.Failure(result.Error!);
        }
        try
        {
            var value = JsonSerializer.Deserialize<T>(result.Value ?? "null");
            return value is null ? ApiResult<T>.Failure(new(ApiErrorCode.Internal, "応答を読み取れませんでした。再読み込みしてください。")) : ApiResult<T>.Success(value);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Failure(new(ApiErrorCode.Internal, "応答を読み取れませんでした。再読み込みしてください。"));
        }
    }

    private static ApiResult<string> Failure(HttpStatusCode status) => ApiResult<string>.Failure(status switch
    {
        HttpStatusCode.Unauthorized => new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました"),
        HttpStatusCode.Forbidden => new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。"),
        _ => new(ApiErrorCode.Internal, "通信に失敗しました。再読み込みしてください。"),
    });

    private sealed record AntiforgeryToken(string Token);
}
