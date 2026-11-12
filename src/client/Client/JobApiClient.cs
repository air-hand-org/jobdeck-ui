using HttpMethod = System.Net.Http.HttpMethod;
using ui.Models;

namespace ui.Client;

public sealed record ListJobsResult(IReadOnlyList<JobView> Jobs, string NextPageToken);

public sealed class JobApiClient(BffTransport transport)
{
    public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => JobAsync(HttpMethod.Post, "bff/jobs", model, cancellationToken);
    public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default) => JobAsync(HttpMethod.Put, $"bff/jobs/{Uri.EscapeDataString(jobId)}", model, cancellationToken);
    public Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => JobAsync(HttpMethod.Get, $"bff/jobs/{Uri.EscapeDataString(jobId)}", null, cancellationToken);

    private Task<ApiResult<JobView>> JobAsync(HttpMethod method, string path, object? model, CancellationToken cancellationToken) =>
        transport.SendDataAsync<JobView>(method, path, model, cancellationToken);

    public async Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var result = await transport.SendAsync(HttpMethod.Delete, $"bff/jobs/{Uri.EscapeDataString(jobId)}", cancellationToken: cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.Error!);
    }

    public Task<ApiResult<ListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default) =>
        transport.SendDataAsync<ListJobsResult>(HttpMethod.Get, $"bff/jobs?pageSize={pageSize}&pageToken={Uri.EscapeDataString(pageToken)}", cancellationToken: cancellationToken);
}
