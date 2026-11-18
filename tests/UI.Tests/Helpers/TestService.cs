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
