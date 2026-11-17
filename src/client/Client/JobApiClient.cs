using HttpMethod = System.Net.Http.HttpMethod;
using ui.Models;
using Grpc.Core;
using Jobdeck.Job.V1;

namespace ui.Client;

public sealed record ListJobsResult(IReadOnlyList<JobView> Jobs, string NextPageToken);

public sealed class JobApiClient(BffTransport transport, JobService.JobServiceClient client)
{
    public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => JobAsync(HttpMethod.Post, "bff/jobs", model, cancellationToken);
    public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default) => JobAsync(HttpMethod.Put, $"bff/jobs/{Uri.EscapeDataString(jobId)}", model, cancellationToken);
    public async Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.GetJobAsync(
                new GetJobRequest { JobId = jobId }
                , cancellationToken: cancellationToken
            );
            return ApiResult<JobView>.Success(new JobView(result.Job));
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<JobView>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<JobView>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure<JobView>(),
            };
        }
        catch (Exception)
        {
            return Failure<JobView>();
        }
    }

    private static ApiResult<T> Failure<T>() => ApiResult<T>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));

    private Task<ApiResult<JobView>> JobAsync(HttpMethod method, string path, object? model, CancellationToken cancellationToken) =>
        transport.SendDataAsync<JobView>(method, path, model, cancellationToken);

    public async Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var result = await transport.SendAsync(HttpMethod.Delete, $"bff/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken: cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.Error!);
    }

    public async Task<ApiResult<ListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.ListJobsAsync(
                new ListJobsRequest { PageSize = pageSize, PageToken = pageToken }
                , cancellationToken: cancellationToken
            );
            return ApiResult<ListJobsResult>.Success(
                new ListJobsResult(
                    [.. result.Jobs.Select(j => new JobView(j))]
                    , result.NextPageToken
                )
            );
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<ListJobsResult>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<ListJobsResult>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure<ListJobsResult>(),
            };
        }
        catch (Exception)
        {
            return Failure<ListJobsResult>();
        }
    }
}
