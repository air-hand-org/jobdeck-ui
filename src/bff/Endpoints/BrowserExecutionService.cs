using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Telemetry;

namespace ui.Endpoints;

// FIXME
public sealed class BrowserExecutionService(ExecutionService.ExecutionServiceClient client, ILogger<BrowserExecutionService> logger) : ExecutionService.ExecutionServiceBase
{
    public override async Task<DoExecutionResponse> DoExecution(DoExecutionRequest request, ServerCallContext context)
    {
        using var activity = UiTelemetry.Source.StartActivity("execution.do", ActivityKind.Internal);
        activity?.SetTag("jobdeck.execution.job_id", request.JobId);

        try
        {
            return await client.DoExecutionAsync(
                request,
                cancellationToken: context.CancellationToken
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


    public override async Task<ListExecutionsResponse> ListExecutions(ListExecutionsRequest request, ServerCallContext context)
    {
        using var activity = UiTelemetry.Source.StartActivity("execution.list", ActivityKind.Internal);
        activity?.SetTag("jobdeck.execution.job_id", request.JobId);

        try
        {
            return await client.ListExecutionsAsync(
                request,
                cancellationToken: context.CancellationToken
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
