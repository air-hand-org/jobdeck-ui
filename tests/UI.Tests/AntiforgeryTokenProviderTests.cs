using Grpc.Core;
using Jobdeck.Bff.V1;
using ui.Client.Antiforgery;

namespace UI.Tests;

public sealed class AntiforgeryTokenProviderTests
{
    private sealed class FakeAntiforgeryClient(
        Func<GetAntiforgeryTokenRequest, CancellationToken, AsyncUnaryCall<GetAntiforgeryTokenResponse>> handler
    ) : AntiforgeryService.AntiforgeryServiceClient
    {
        public override AsyncUnaryCall<GetAntiforgeryTokenResponse> GetAntiforgeryTokenAsync(
            GetAntiforgeryTokenRequest request,
            Metadata? metadata = null,
            DateTime? deadline = null,
            CancellationToken cancellationToken = default
        ) => handler(request, cancellationToken);
    }

    [Fact]
    public async Task UseCacheWhenGetAntiforgeryTokenConcurrently()
    {
        // FIXME: 速度に問題なければTestServerScopeに書き換える
        var fetchCount = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<GetAntiforgeryTokenResponse> FetchAsync(CancellationToken ct)
        {
            var number = Interlocked.Increment(ref fetchCount);
            await release.Task.WaitAsync(ct);
            return new GetAntiforgeryTokenResponse{
                RequestToken = $"this is test {number}"
            };
        };
        var fake = new FakeAntiforgeryClient((request, ct) =>
            {
                return new AsyncUnaryCall<GetAntiforgeryTokenResponse>(
                    FetchAsync(ct),
                    Task.FromResult(new Metadata()),
                    () => Status.DefaultSuccess,
                    () => Metadata.Empty,
                    () => { }
                );
            });
        var provider = new AntiforgeryTokenProvider(fake);

        var first = provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var second = provider.GetTokenAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, fetchCount);

        release.SetResult();

        var tokens = await Task.WhenAll(first, second);

        Assert.All(tokens, t => Assert.Equal("this is test 1", t));
        Assert.Equal(1, fetchCount);
    }
}
