using Grpc.Core;

using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;

namespace UI.Tests.Helpers;

public sealed class TestAntiforgeryService : AntiforgeryService.AntiforgeryServiceBase
{
    public Func<GetAntiforgeryTokenRequest, ServerCallContext, Task<GetAntiforgeryTokenResponse>>? OnGetAntiforgeryToken { get; init; }

    public override Task<GetAntiforgeryTokenResponse> GetAntiforgeryToken(
        GetAntiforgeryTokenRequest request,
        ServerCallContext context
    )
    {
        return OnGetAntiforgeryToken is { } handler ? handler(request, context) : throw new NotImplementedException($"{nameof(GetAntiforgeryToken)} is not implemented");
    }
}

public sealed class TestJobService : JobService.JobServiceBase
{
    public Func<CreateJobRequest, ServerCallContext, Task<CreateJobResponse>>? OnCreateJob { get; init; }

    public override Task<CreateJobResponse> CreateJob(CreateJobRequest request, ServerCallContext context)
        => OnCreateJob is { } handler ? handler(request, context) : throw new NotImplementedException("CreateJob is not implemented");

    public Func<UpdateJobRequest, ServerCallContext, Task<UpdateJobResponse>>? OnUpdateJob { get; init; }

    public override Task<UpdateJobResponse> UpdateJob(UpdateJobRequest request, ServerCallContext context)
        => OnUpdateJob is { } handler ? handler(request, context) : throw new NotImplementedException("UpdateJob is not implemented");

    public Func<DeleteJobRequest, ServerCallContext, Task<DeleteJobResponse>>? OnDeleteJob { get; init; }

    public override Task<DeleteJobResponse> DeleteJob(DeleteJobRequest request, ServerCallContext context)
        => OnDeleteJob is { } handler ? handler(request, context) : throw new NotImplementedException("DeleteJob is not implemented");

    public Func<GetJobRequest, ServerCallContext, Task<GetJobResponse>>? OnGetJob { get; init; }
    public Func<ListJobsRequest, ServerCallContext, Task<ListJobsResponse>>? OnListJobs { get; init; }

    public override Task<GetJobResponse> GetJob(
        GetJobRequest request,
        ServerCallContext context
    )
    {
        return OnGetJob is { } handler ? handler(request, context) : throw new NotImplementedException($"{nameof(GetJob)} is not implemented");
    }

    public override Task<ListJobsResponse> ListJobs(
        ListJobsRequest request,
        ServerCallContext context
    )
    {
        return OnListJobs is { } handler ? handler(request, context) : throw new NotImplementedException($"{nameof(ListJobs)} is not implemented");
    }
}

public sealed class TestExecutionService : ExecutionService.ExecutionServiceBase
{
    public Func<DoExecutionRequest, ServerCallContext, Task<DoExecutionResponse>>? OnDoExecution { get; init; }

    public override Task<DoExecutionResponse> DoExecution(DoExecutionRequest request, ServerCallContext context)
        => OnDoExecution is { } handler ? handler(request, context) : throw new NotImplementedException("DoExecution is not implemented");

    public Func<GetExecutionRequest, ServerCallContext, Task<GetExecutionResponse>>? OnGetExecution { get; init; }
    public Func<ListExecutionsRequest, ServerCallContext, Task<ListExecutionsResponse>>? OnListExecutions { get; init; }

    public override Task<GetExecutionResponse> GetExecution(
        GetExecutionRequest request,
        ServerCallContext context
    )
    {
        return OnGetExecution is { } handler ? handler(request, context) : throw new NotImplementedException($"{nameof(GetExecution)} is not implemented");
    }

    public override Task<ListExecutionsResponse> ListExecutions(
        ListExecutionsRequest request,
        ServerCallContext context
    )
    {
        return OnListExecutions is { } handler ? handler(request, context) : throw new NotImplementedException($"{nameof(ListExecutions)} is not implemented");
    }
}
