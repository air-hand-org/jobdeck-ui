using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Bff.Telemetry;

namespace ui.Bff.Client;

public sealed class BackendExecutionClient(ExecutionService.ExecutionServiceClient execs, ILogger<BackendExecutionClient> logger)
{
    public async Task<DoExecutionResponse> DoExecutionAsync(DoExecutionRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("execution.do", ActivityKind.Internal);
        activity?.SetTag("jobdeck.execution.job_id", request.JobId);

        try
        {
            return await execs.DoExecutionAsync(
                request,
                cancellationToken: cancellationToken
            );
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Execution API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            throw;
        }
    }

    public async Task<ListExecutionsResponse> ListExecutionsAsync(ListExecutionsRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("execution.list", ActivityKind.Internal);
        activity?.SetTag("jobdeck.execution.job_id", request.JobId);

        try
        {
            return await execs.ListExecutionsAsync(
                request,
                cancellationToken: cancellationToken
            );
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Execution API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            throw;
        }
    }
}
