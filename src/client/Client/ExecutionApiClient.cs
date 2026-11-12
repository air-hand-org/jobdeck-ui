using HttpMethod = System.Net.Http.HttpMethod;
using ui.Models;

namespace ui.Client;

public sealed record ListExecutionsResult(List<ExecutionView> Executions, string NextPageToken);

public sealed class ExecutionApiClient(BffTransport transport)
{
    public async Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var result = await transport.SendAsync(HttpMethod.Post, $"bff/jobs/{Uri.EscapeDataString(jobId)}/executions", cancellationToken: cancellationToken);
        return result.IsSuccess ? ApiResult.Success() : ApiResult.Failure(result.Error!);
    }

    public Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default) =>
        transport.SendDataAsync<ListExecutionsResult>(HttpMethod.Get, $"bff/jobs/{Uri.EscapeDataString(jobId)}/executions?pageSize={pageSize}&pageToken={Uri.EscapeDataString(pageToken)}", cancellationToken: cancellationToken);
}
