using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Telemetry;

namespace ui.Client;

public sealed class BackendJobClient(JobService.JobServiceClient jobs, ILogger<BackendJobClient> logger)
{
    public async Task<CreateJobResponse> CreateJobAsync(CreateJobRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.create", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.name", request.Name);

        try
        {
            return await jobs.CreateJobAsync(
                request,
                cancellationToken: cancellationToken
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

    public async Task<UpdateJobResponse> UpdateJobAsync(UpdateJobRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.update", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", request.JobId);
        activity?.SetTag("jobdeck.job.name", request.Name);

        try
        {
            return await jobs.UpdateJobAsync(
                request,
                cancellationToken: cancellationToken
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

    public async Task<SetJobEnabledResponse> SetJobEnabledAsync(SetJobEnabledRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.enabled", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", request.JobId);
        activity?.SetTag("jobdeck.job.enabled", request.Enabled);

        try
        {
            return await jobs.SetJobEnabledAsync(
                request,
                cancellationToken: cancellationToken
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

    public async Task<DeleteJobResponse> DeleteJobAsync(DeleteJobRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.delete", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", request.JobId);

        try
        {
            return await jobs.DeleteJobAsync(
                request,
                cancellationToken: cancellationToken
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

    public async Task<GetJobResponse> GetJobAsync(GetJobRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.get", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", request.JobId);

        try
        {
            return await jobs.GetJobAsync(
                request,
                cancellationToken: cancellationToken
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

    public async Task<ListJobsResponse> ListJobsAsync(ListJobsRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.list", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.list_page_size", request.PageSize);

        try
        {
            return await jobs.ListJobsAsync(
                request,
                cancellationToken: cancellationToken
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
