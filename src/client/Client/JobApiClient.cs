using Grpc.Core;
using Jobdeck.Job.V1;

using ui.Models;

namespace ui.Client;

public sealed record ListJobsResult(IReadOnlyList<JobView> Jobs, string NextPageToken);

public sealed class JobApiClient(JobService.JobServiceClient client)
{
    public async Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.CreateJobAsync(
                new CreateJobRequest
                {
                    Name = model.Name,
                    Spec = model.ToJobSpec()
                },
                cancellationToken: cancellationToken
            );
            return ApiResult<JobView>.Success(new(result.Job));
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

    public async Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.UpdateJobAsync(
                new UpdateJobRequest
                {
                    JobId = jobId,
                    Name = model.Name,
                    Spec = model.ToJobSpec()
                },
                cancellationToken: cancellationToken
            );
            return ApiResult<JobView>.Success(new(result.Job));
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

    public async Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.DeleteJobAsync(
                new DeleteJobRequest { JobId = jobId }
                , cancellationToken: cancellationToken
            );
            return ApiResult.Success();
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure(),
            };
        }
        catch (Exception)
        {
            return Failure();
        }
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
                new(
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

    private static ApiResult<T> Failure<T>() => ApiResult<T>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));
    private static ApiResult Failure() => ApiResult.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));
}
