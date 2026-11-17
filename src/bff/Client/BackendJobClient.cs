using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Auth;
using ui.Models;
using ui.Telemetry;

namespace ui.Client;

public sealed record BackendListJobsResult(List<Job> Jobs, string NextPageToken);

public sealed class BackendJobClient(JobService.JobServiceClient jobs, ILogger<BackendJobClient> logger)
{
    public async Task<ApiResult<Job>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.create", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.name", model.Name);

        try
        {
            // FIXME: use AddCallCredentials
            var response = await jobs.CreateJobAsync(
                new CreateJobRequest
                {
                    Name = model.Name,
                    Spec = model.ToJobSpec(),
                },
                cancellationToken: cancellationToken
            );

            return ApiResult<Job>.Success(response.Job);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<Job>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<Job>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.update", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", jobId);
        activity?.SetTag("jobdeck.job.name", model.Name);

        try
        {
            var response = await jobs.UpdateJobAsync(
                new UpdateJobRequest
                {
                    JobId = jobId,
                    Name = model.Name,
                    Spec = model.ToJobSpec(),
                },
                cancellationToken: cancellationToken
            );
            return ApiResult<Job>.Success(response.Job);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<Job>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.delete", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", jobId);

        try
        {
            var response = await jobs.DeleteJobAsync(
                new DeleteJobRequest
                {
                    JobId = jobId,
                },
                cancellationToken: cancellationToken
            );
            return ApiResult.Success();
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<Job>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.get", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.id", jobId);

        try
        {
            var response = await jobs.GetJobAsync(
                new GetJobRequest
                {
                    JobId = jobId,
                },
                cancellationToken: cancellationToken
            );
            return ApiResult<Job>.Success(response.Job);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<Job>.Failure(BackendErrors.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<BackendListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("job.list", ActivityKind.Internal);
        activity?.SetTag("jobdeck.job.list_page_size", pageSize);

        try
        {
            var response = await jobs.ListJobsAsync(
                new ListJobsRequest
                {
                    PageSize = pageSize,
                    PageToken = pageToken,
                },
                cancellationToken: cancellationToken
            );
            return ApiResult<BackendListJobsResult>.Success(new BackendListJobsResult(
              [.. response.Jobs],
              response.NextPageToken
            ));
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Job API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<BackendListJobsResult>.Failure(BackendErrors.FromRPCException(ex));
        }
    }
}
