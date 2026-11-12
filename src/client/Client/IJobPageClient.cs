using Jobdeck.Job.V1;
using ui.Models;

namespace ui.Client;

// Browser and request-scoped prerendering use the same page-facing operations.
public interface IJobPageClient
{
    Task<ApiResult<ListJobsResponse>> ListJobsAsync(CancellationToken cancellationToken = default);
    Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default);
    Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default);
    Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default);
    Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);
    Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default);
}

public sealed class BrowserJobPageClient(BrowserJobClient list, JobApiClient jobs, ExecutionApiClient executions) : IJobPageClient
{
    public Task<ApiResult<ListJobsResponse>> ListJobsAsync(CancellationToken cancellationToken = default) => list.ListJobsAsync(cancellationToken);
    public Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => jobs.GetJobAsync(jobId, cancellationToken);
    public Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default) => executions.ListExecutionsAsync(jobId, pageSize, pageToken, cancellationToken);
    public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => jobs.CreateJobAsync(model, cancellationToken);
    public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default) => jobs.UpdateJobAsync(jobId, model, cancellationToken);
    public Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default) => jobs.DeleteJobAsync(jobId, cancellationToken);
    public Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default) => executions.DoExecutionAsync(jobId, cancellationToken);
}
