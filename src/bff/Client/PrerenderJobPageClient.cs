using Google.Protobuf;
using Jobdeck.Job.V1;
using ui.Models;

namespace ui.Client;

// Scoped to the HTML request: no self-HTTP calls, browser credentials or UI session state.
public sealed class PrerenderJobPageClient(BackendJobClient jobs, BackendExecutionClient executions) : IJobPageClient
{
    public async Task<ApiResult<ListJobsResponse>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        var result = await jobs.ListJobsAsync(100, cancellationToken: cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiResult<ListJobsResponse>.Failure(result.Error!);
        }
        var response = new ListJobsResponse { NextPageToken = result.Value!.NextPageToken };
        response.Jobs.AddRange(result.Value.Jobs);
        return ApiResult<ListJobsResponse>.Success(response);
    }

    public async Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var result = await jobs.GetJobAsync(jobId, cancellationToken);
        return result.IsSuccess
            ? ApiResult<JobView>.Success(new(result.Value!.Id, result.Value.Name, result.Value.Enabled, JobMapping.FromProto(result.Value)))
            : ApiResult<JobView>.Failure(result.Error!);
    }

    public async Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        var result = await executions.ListExecutionsAsync(jobId, pageSize, pageToken, cancellationToken);
        return result.IsSuccess
            ? ApiResult<ListExecutionsResult>.Success(new(result.Value!.Executions.Select(execution => new ExecutionView(
                execution.Id, execution.Attempt, execution.Status.ToString(), execution.CreatedAt?.ToDateTime(), execution.UpdatedAt?.ToDateTime(),
                execution.Result is null ? "-" : JsonFormatter.Default.Format(execution.Result))).ToList(), result.Value.NextPageToken))
            : ApiResult<ListExecutionsResult>.Failure(result.Error!);
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
