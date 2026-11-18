using Grpc.Core;
using Jobdeck.Job.V1;

namespace ui.Client.Api;

public sealed record ListExecutionsResult(List<Models.ExecutionView> Executions, string NextPageToken);

public sealed class ExecutionApiClient(ExecutionService.ExecutionServiceClient execs)
{
    public async Task<ApiResult<DoExecutionResponse>> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await execs.DoExecutionAsync(
                new DoExecutionRequest { JobId = jobId },
                cancellationToken: cancellationToken
            );
            return ApiResult<DoExecutionResponse>.Success(result);
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<DoExecutionResponse>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<DoExecutionResponse>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure<DoExecutionResponse>(),
            };
        }
        catch (Exception)
        {
            return Failure<DoExecutionResponse>();
        }
    }

    public async Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await execs.ListExecutionsAsync(
                new ListExecutionsRequest { JobId = jobId, PageSize = pageSize, PageToken = pageToken },
                cancellationToken: cancellationToken
            );
            return ApiResult<ListExecutionsResult>.Success(
                new ListExecutionsResult([.. result.Executions.Select(e => new Models.ExecutionView(e))]
                , result.NextPageToken)
            );
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<ListExecutionsResult>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<ListExecutionsResult>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure<ListExecutionsResult>(),
            };
        }
        catch (Exception)
        {
            return Failure<ListExecutionsResult>();
        }
    }

    private static ApiResult<T> Failure<T>() => ApiResult<T>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。再読み込みしてください"));
}
