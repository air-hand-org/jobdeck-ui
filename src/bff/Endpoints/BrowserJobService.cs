using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Client;
using ui.Telemetry;

namespace ui.Endpoints;

// Only ListJobs is implemented in this trial. Other generated RPCs remain UNIMPLEMENTED.
public sealed class BrowserJobService(JobService.JobServiceClient client, ILogger<BrowserJobService> logger) : JobService.JobServiceBase
{
    public override async Task<ListJobsResponse> ListJobs(ListJobsRequest request, ServerCallContext context)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.list", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.list_page_size", request.PageSize);

        try
        {
            return await client.ListJobsAsync(
                request,
                cancellationToken: context.CancellationToken
            );
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            throw;
        }
    }

    public override async Task<GetJobResponse> GetJob(GetJobRequest request, ServerCallContext context)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.get", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", request.JobId);

        try
        {
            return await client.GetJobAsync(
                request,
                cancellationToken: context.CancellationToken
            );
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            throw;
        }
    }
}
