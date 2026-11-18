using Grpc.Core;
using Jobdeck.Bff.V1;
using Microsoft.Extensions.DependencyInjection;
using ui.Client.Antiforgery;
using UI.Tests.Helpers;

namespace UI.Tests;

public sealed class AntiforgeryTokenProviderTests
{
    [Fact]
    public async Task ConcurrentCallsShareFetchAndCache()
    {
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scope = await TestServerScope.CreateAsync(new TestAntiforgeryService
        {
            OnGetAntiforgeryToken = async (_, context) =>
            {
                var call = Interlocked.Increment(ref calls);
                entered.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
                return new GetAntiforgeryTokenResponse { RequestToken = $"shared-token-{call}" };
            }
        }, new TestJobService(), new TestExecutionService(), TestContext.Current.CancellationToken);
        var provider = scope.Services.GetRequiredService<IAntiforgeryTokenProvider>();
        var first = provider.GetTokenAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var second = provider.GetTokenAsync(TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.All(await Task.WhenAll(first, second), token => Assert.Equal("shared-token-1", token));
        Assert.Equal("shared-token-1", await provider.GetTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellingWaitingCallDoesNotCancelActiveFetch()
    {
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scope = await TestServerScope.CreateAsync(new TestAntiforgeryService
        {
            OnGetAntiforgeryToken = async (_, context) =>
            {
                Interlocked.Increment(ref calls);
                entered.SetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), context.CancellationToken);
                return new GetAntiforgeryTokenResponse { RequestToken = "active-token" };
            }
        }, new TestJobService(), new TestExecutionService(), TestContext.Current.CancellationToken);
        var provider = scope.Services.GetRequiredService<IAntiforgeryTokenProvider>();
        var first = provider.GetTokenAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var waiting = provider.GetTokenAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(first.IsCompleted);
        release.SetResult();
        Assert.Equal("active-token", await first);
        Assert.Equal("active-token", await provider.GetTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrEmptyFetchCanBeRetried(bool empty)
    {
        var calls = 0;
        await using var scope = await TestServerScope.CreateAsync(new TestAntiforgeryService
        {
            OnGetAntiforgeryToken = (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return empty ? Task.FromResult(new GetAntiforgeryTokenResponse())
                        : Task.FromException<GetAntiforgeryTokenResponse>(new RpcException(new Status(StatusCode.Unavailable, "unavailable")));
                }
                return Task.FromResult(new GetAntiforgeryTokenResponse { RequestToken = "retry-token" });
            }
        }, new TestJobService(), new TestExecutionService(), TestContext.Current.CancellationToken);
        var provider = scope.Services.GetRequiredService<IAntiforgeryTokenProvider>();
        if (empty)
        {
            Assert.Equal("", await provider.GetTokenAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            await Assert.ThrowsAsync<RpcException>(() => provider.GetTokenAsync(TestContext.Current.CancellationToken));
        }
        Assert.Equal("retry-token", await provider.GetTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal("retry-token", await provider.GetTokenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, calls);
    }
}
