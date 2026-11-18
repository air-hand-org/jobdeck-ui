using Grpc.Core;
using Jobdeck.Job.V1;

namespace ui.Bff.Endpoints;

public sealed class BrowserJobService(Client.BackendJobClient client) : JobService.JobServiceBase
{
    public override async Task<ListJobsResponse> ListJobs(ListJobsRequest request, ServerCallContext context) => await client.ListJobsAsync(request, context.CancellationToken);
    public override async Task<GetJobResponse> GetJob(GetJobRequest request, ServerCallContext context) => await client.GetJobAsync(request, context.CancellationToken);
    public override async Task<CreateJobResponse> CreateJob(CreateJobRequest request, ServerCallContext context) => await client.CreateJobAsync(request, context.CancellationToken);
    public override async Task<UpdateJobResponse> UpdateJob(UpdateJobRequest request, ServerCallContext context) => await client.UpdateJobAsync(request, context.CancellationToken);
    public override async Task<DeleteJobResponse> DeleteJob(DeleteJobRequest request, ServerCallContext context) => await client.DeleteJobAsync(request, context.CancellationToken);
    public override async Task<SetJobEnabledResponse> SetJobEnabled(SetJobEnabledRequest request, ServerCallContext context) => await client.SetJobEnabledAsync(request, context.CancellationToken);
}
