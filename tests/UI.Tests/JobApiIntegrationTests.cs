using Grpc.Core;
using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;
using Microsoft.Extensions.DependencyInjection;
using ui.Client.Api;
using ui.Client.Models;
using UI.Tests.Helpers;

namespace UI.Tests;

public sealed class JobApiIntegrationTests
{
    private static HttpJobFormModel Form() => new()
    {
        Name = "example",
        Url = "https://example.com/run",
        Method = JobHttpMethod.Post,
        ExpectedStatusCodes = [201, 204],
        MaxAttempts = 3
    };

    [Fact]
    public async Task CrudRoundTripPreservesRequestsAndResponses()
    {
        var calls = new List<string>();
        var form = Form();
        var job = new Job { Id = "job-id", Name = form.Name, Enabled = true, Spec = form.ToJobSpec() };
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService
        {
            OnCreateJob = (request, context) =>
            {
                AssertToken(context); calls.Add("create");
                Assert.Equal(form.Name, request.Name); Assert.Equal(form.ToJobSpec(), request.Spec);
                return Task.FromResult(new CreateJobResponse { Job = job });
            },
            OnUpdateJob = (request, context) =>
            {
                AssertToken(context); calls.Add("update");
                Assert.Equal("job-id", request.JobId); Assert.Equal(form.Name, request.Name);
                Assert.Equal(form.ToJobSpec(), request.Spec);
                return Task.FromResult(new UpdateJobResponse { Job = job });
            },
            OnGetJob = (request, context) =>
            {
                AssertToken(context); calls.Add("get"); Assert.Equal("job-id", request.JobId);
                return Task.FromResult(new GetJobResponse { Job = job });
            },
            OnListJobs = (request, context) =>
            {
                AssertToken(context); calls.Add("list");
                Assert.Equal(17, request.PageSize); Assert.Equal("page", request.PageToken);
                return Task.FromResult(new ListJobsResponse { Jobs = { job }, NextPageToken = "next" });
            },
            OnDeleteJob = (request, context) =>
            {
                AssertToken(context); calls.Add("delete"); Assert.Equal("job-id", request.JobId);
                return Task.FromResult(new DeleteJobResponse());
            }
        }, new TestExecutionService(), TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<JobApiClient>();
        var created = await client.CreateJobAsync(form, TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess); Assert.Equal("job-id", created.Value!.Id);
        Assert.Equal(JobHttpMethod.Post, created.Value.Form.Method);
        Assert.Equal(new[] { 201, 204 }, created.Value.Form.ExpectedStatusCodes);
        Assert.Equal(3, created.Value.Form.MaxAttempts);
        Assert.True((await client.UpdateJobAsync("job-id", form, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal("example", (await client.GetJobAsync("job-id", TestContext.Current.CancellationToken)).Value!.Name);
        var listed = await client.ListJobsAsync(17, "page", TestContext.Current.CancellationToken);
        Assert.True(listed.IsSuccess); Assert.Equal("job-id", Assert.Single(listed.Value!.Jobs).Id);
        Assert.Equal("next", listed.Value.NextPageToken);
        Assert.True((await client.DeleteJobAsync("job-id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(new[] { "create", "update", "get", "list", "delete" }, calls);
    }

    [Fact]
    public async Task MissingJobPayloadIsRecoverableOnNextRead()
    {
        var calls = 0;
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService
        {
            OnGetJob = (_, _) => Task.FromResult(++calls == 1 ? new GetJobResponse()
                : new GetJobResponse { Job = new Job { Id = "recovered", Spec = Form().ToJobSpec() } })
        }, new TestExecutionService(), TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<JobApiClient>();
        Assert.False((await client.GetJobAsync("id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, calls);
        Assert.Equal("recovered", (await client.GetJobAsync("id", TestContext.Current.CancellationToken)).Value!.Id);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedTokenFetchPreventsMutationAndNextExplicitCallRetriesFetch()
    {
        var fetches = 0;
        var writes = 0;
        await using var scope = await TestServerScope.CreateAsync(new TestAntiforgeryService
        {
            OnGetAntiforgeryToken = (_, _) => ++fetches == 1
                ? Task.FromException<GetAntiforgeryTokenResponse>(new RpcException(new Status(StatusCode.Unavailable, "private-token")))
                : Task.FromResult(new GetAntiforgeryTokenResponse { RequestToken = "request-token" })
        }, new TestJobService
        {
            OnDeleteJob = (_, context) =>
            {
                AssertToken(context); ++writes; return Task.FromResult(new DeleteJobResponse());
            }
        }, new TestExecutionService(), TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<JobApiClient>();
        Assert.False((await client.DeleteJobAsync("id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(0, writes); Assert.Equal(1, fetches);
        Assert.True((await client.DeleteJobAsync("id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, writes); Assert.Equal(2, fetches);
    }

    [Theory]
    [InlineData("CreateJob", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("CreateJob", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("CreateJob", StatusCode.Unavailable, ApiErrorCode.Internal)]
    [InlineData("UpdateJob", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("UpdateJob", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("UpdateJob", StatusCode.Unavailable, ApiErrorCode.Internal)]
    [InlineData("GetJob", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("GetJob", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("GetJob", StatusCode.Unavailable, ApiErrorCode.Internal)]
    [InlineData("ListJobs", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("ListJobs", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("ListJobs", StatusCode.Unavailable, ApiErrorCode.Internal)]
    [InlineData("DeleteJob", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("DeleteJob", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("DeleteJob", StatusCode.Unavailable, ApiErrorCode.Internal)]
    public async Task RpcFailuresAreMappedWithoutLeakingDetailsOrRetrying(string operation, StatusCode status, ApiErrorCode expected)
    {
        var calls = 0;
        Task<T> Fail<T>(ServerCallContext context)
        {
            AssertToken(context); ++calls;
            return Task.FromException<T>(new RpcException(new Status(status, "private-owner-token")));
        }
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService
        {
            OnCreateJob = (_, context) => Fail<CreateJobResponse>(context),
            OnUpdateJob = (_, context) => Fail<UpdateJobResponse>(context),
            OnGetJob = (_, context) => Fail<GetJobResponse>(context),
            OnListJobs = (_, context) => Fail<ListJobsResponse>(context),
            OnDeleteJob = (_, context) => Fail<DeleteJobResponse>(context)
        }, new TestExecutionService(), TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<JobApiClient>();
        var ct = TestContext.Current.CancellationToken;
        var error = operation switch
        {
            "CreateJob" => (await client.CreateJobAsync(Form(), ct)).Error,
            "UpdateJob" => (await client.UpdateJobAsync("id", Form(), ct)).Error,
            "GetJob" => (await client.GetJobAsync("id", ct)).Error,
            "ListJobs" => (await client.ListJobsAsync(10, cancellationToken: ct)).Error,
            "DeleteJob" => (await client.DeleteJobAsync("id", ct)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Assert.NotNull(error); Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("private-owner-token", error.Message);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task EmptyTokenIsRejectedBeforeMutationByRealAntiforgeryValidation()
    {
        var writes = 0;
        await using var scope = await TestServerScope.CreateAsync(new TestAntiforgeryService
        {
            OnGetAntiforgeryToken = (_, _) => Task.FromResult(new GetAntiforgeryTokenResponse())
        }, new TestJobService
        {
            OnDeleteJob = (_, _) =>
            {
                ++writes; return Task.FromResult(new DeleteJobResponse());
            }
        }, new TestExecutionService(), TestContext.Current.CancellationToken, validateAntiforgery: true);
        var result = await scope.Services.GetRequiredService<JobApiClient>().DeleteJobAsync("id", TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task FailedListCanBeExplicitlyRetried()
    {
        var reads = 0;
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService
        {
            OnListJobs = (_, _) => ++reads == 1
                ? Task.FromException<ListJobsResponse>(new RpcException(new Status(StatusCode.Unavailable, "private")))
                : Task.FromResult(new ListJobsResponse())
        }, new TestExecutionService(), TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<JobApiClient>();
        Assert.False((await client.ListJobsAsync(10, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, reads);
        Assert.True((await client.ListJobsAsync(10, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, reads);
    }

    private static TestAntiforgeryService Tokens() => new()
    {
        OnGetAntiforgeryToken = (_, context) =>
        {
            Assert.Null(context.RequestHeaders.GetValue("x-csrf-token"));
            return Task.FromResult(new GetAntiforgeryTokenResponse { RequestToken = "request-token" });
        }
    };

    private static void AssertToken(ServerCallContext context)
        => Assert.Equal("request-token", context.RequestHeaders.GetValue("x-csrf-token"));
}
