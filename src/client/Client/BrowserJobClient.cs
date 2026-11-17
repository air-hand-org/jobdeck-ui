using Grpc.Core;
using Jobdeck.Job.V1;

namespace ui.Client;

public sealed class BrowserJobClient(JobService.JobServiceClient jobs)
{
    public async Task<ApiResult<ListJobsResponse>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobs.ListJobsAsync(new ListJobsRequest { PageSize = 100 }, cancellationToken: cancellationToken);
            return ApiResult<ListJobsResponse>.Success(result);
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<ListJobsResponse>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure<ListJobsResponse>(),
            };
        }
        catch (Exception)
        {
            return Failure<ListJobsResponse>();
        }
    }

    private static ApiResult<T> Failure<T>() => ApiResult<T>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));
}
