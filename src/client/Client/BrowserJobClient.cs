using System.Net.Http.Json;
using Grpc.Core;
using Jobdeck.Job.V1;

namespace ui.Client;

public sealed class BrowserJobClient(HttpClient http, JobService.JobServiceClient jobs)
{
    public async Task<ApiResult<ListJobsResponse>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync("bff/antiforgery", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました"));
            }
            response.EnsureSuccessStatusCode();
            var csrf = await response.Content.ReadFromJsonAsync<CsrfToken>(cancellationToken);
            if (string.IsNullOrWhiteSpace(csrf?.Token))
            {
                return Failure();
            }
            var result = await jobs.ListJobsAsync(new ListJobsRequest { PageSize = 100 }, new Metadata { { "x-csrf-token", csrf.Token } }, cancellationToken: cancellationToken);
            return ApiResult<ListJobsResponse>.Success(result);
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure(),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            return Failure();
        }
    }

    private static ApiResult<ListJobsResponse> Failure() => ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));
    private sealed record CsrfToken(string Token);
}
