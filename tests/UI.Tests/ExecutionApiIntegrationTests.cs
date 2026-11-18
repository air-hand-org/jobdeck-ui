using Grpc.Core;
using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;
using Microsoft.Extensions.DependencyInjection;
using ui.Client.Api;
using ui.Client.Models;
using UI.Tests.Helpers;

namespace UI.Tests;

public sealed class ExecutionApiIntegrationTests
{

    [Theory]
    [InlineData("DoExecution", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("DoExecution", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("DoExecution", StatusCode.Unavailable, ApiErrorCode.Internal)]
    [InlineData("ListExecutions", StatusCode.Unauthenticated, ApiErrorCode.Unauthenticated)]
    [InlineData("ListExecutions", StatusCode.PermissionDenied, ApiErrorCode.PermissionDenied)]
    [InlineData("ListExecutions", StatusCode.Unavailable, ApiErrorCode.Internal)]
    public async Task RpcFailuresAreMappedWithoutLeakingDetailsOrRetrying(string operation, StatusCode status, ApiErrorCode expected)
    {
        var calls = 0;
        Task<T> Fail<T>(ServerCallContext context)
        {
            AssertToken(context); ++calls;
            return Task.FromException<T>(new RpcException(new Status(status, "private-owner-token")));
        }
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService(), new TestExecutionService
        {
            OnDoExecution = (_, context) => Fail<DoExecutionResponse>(context),
            OnListExecutions = (_, context) => Fail<ListExecutionsResponse>(context)
        }, TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<ExecutionApiClient>();
        var ct = TestContext.Current.CancellationToken;
        var error = operation switch
        {
            "DoExecution" => (await client.DoExecutionAsync("id", ct)).Error,
            "ListExecutions" => (await client.ListExecutionsAsync("id", 10, cancellationToken: ct)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Assert.NotNull(error); Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("private-owner-token", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ExecutionRequestsAndPaginationRoundTrip()
    {
        var writes = 0;
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService(), new TestExecutionService
        {
            OnDoExecution = (request, context) =>
            {
                AssertToken(context); Assert.Equal("job-id", request.JobId); ++writes;
                return Task.FromResult(new DoExecutionResponse());
            },
            OnListExecutions = (request, context) =>
            {
                AssertToken(context); Assert.Equal("job-id", request.JobId);
                Assert.Equal(23, request.PageSize); Assert.Equal("page", request.PageToken);
                return Task.FromResult(new ListExecutionsResponse
                {
                    Executions = { new Execution { Id = "execution-id", Attempt = 2, CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UnixEpoch), UpdatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UnixEpoch) } },
                    NextPageToken = "next"
                });
            }
        }, TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<ExecutionApiClient>();
        Assert.True((await client.DoExecutionAsync("job-id", TestContext.Current.CancellationToken)).IsSuccess);
        var result = await client.ListExecutionsAsync("job-id", 23, "page", TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); Assert.Equal("next", result.Value!.NextPageToken);
        var execution = Assert.Single(result.Value.Executions);
        Assert.Equal("execution-id", execution.Id); Assert.Equal(2, execution.Attempt);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task FailedReadCanBeExplicitlyRetried()
    {
        var reads = 0;
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService(), new TestExecutionService
        {
            OnListExecutions = (_, _) => ++reads == 1
                ? Task.FromException<ListExecutionsResponse>(new RpcException(new Status(StatusCode.Unavailable, "private")))
                : Task.FromResult(new ListExecutionsResponse())
        }, TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<ExecutionApiClient>();
        Assert.False((await client.ListExecutionsAsync("id", 10, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, reads);
        Assert.True((await client.ListExecutionsAsync("id", 10, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, reads);
    }
    [Fact]
    public async Task CancellingReadCancelsServerCallAndNextReadSucceeds()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await using var scope = await TestServerScope.CreateAsync(Tokens(), new TestJobService(), new TestExecutionService
        {
            OnListExecutions = async (_, context) =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    using var registration = context.CancellationToken.Register(() => cancelled.TrySetResult());
                    entered.SetResult();
                    await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
                }
                return new ListExecutionsResponse();
            }
        }, TestContext.Current.CancellationToken);
        var client = scope.Services.GetRequiredService<ExecutionApiClient>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = client.ListExecutionsAsync("id", 10, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        Assert.False((await pending).IsSuccess);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True((await client.ListExecutionsAsync("id", 10, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
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
