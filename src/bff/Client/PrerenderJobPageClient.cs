using Google.Protobuf;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Models;

namespace ui.Client;

// Scoped to the HTML request: no self-HTTP calls, browser credentials or UI session state.
public sealed class PrerenderJobPageClient(BackendJobClient jobs, BackendExecutionClient executions) : IJobPageClient
{
    public async Task<ApiResult<ListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobs.ListJobsAsync(
                new ListJobsRequest { PageSize = pageSize, PageToken = pageToken },
                cancellationToken: cancellationToken
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
            return ApiResult<ListJobsResult>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await jobs.GetJobAsync(
                new GetJobRequest { JobId = jobId },
                cancellationToken
            );
            return ApiResult<JobView>.Success(new(result.Job));
        }
        catch (RpcException ex)
        {
            return ApiResult<JobView>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await executions.ListExecutionsAsync(
                new ListExecutionsRequest { JobId = jobId, PageSize = pageSize, PageToken = pageToken },
                cancellationToken
            );
            return ApiResult<ListExecutionsResult>.Success(
                new(
                    [.. result.Executions.Select(e => new ExecutionView(e))]
                    , result.NextPageToken
                )
            );
        }
        catch (RpcException ex)
        {
            return ApiResult<ListExecutionsResult>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    // Prerendering is read-only. Interactive mutations always use the browser BFF.
    public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => throw new NotSupportedException("Prerendering is read-only.");
    public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default) => throw new NotSupportedException("Prerendering is read-only.");
    public Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Prerendering is read-only.");
    public Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException("Prerendering is read-only.");
}

public static class JobPagePrerendering
{
    public static void UsePrivateJobPages(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/jobs"))
            {
                context.Response.Headers.CacheControl = "no-store";
            }
            await next(context);
        });
    }
}
