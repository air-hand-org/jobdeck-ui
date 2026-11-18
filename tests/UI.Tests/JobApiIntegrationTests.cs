using Grpc.Core;
using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;
using Microsoft.Extensions.DependencyInjection;
using ui.Client.Api;
using UI.Tests.Helpers;

namespace UI.Tests;

public sealed class JobApiIntegrationTests
{
    [Fact]
    public async Task MutationsSendAntiforgeryTokenAndAreNotRetried()
    {
        var writes = 0;

        await using var scope = await TestServerScope.CreateAsync(
            new TestAntiforgeryService
            {
                OnGetAntiforgeryToken = (request, context) =>
                {
                    return Task.FromResult(new GetAntiforgeryTokenResponse { RequestToken = "request token" });
                }
            },
            new TestJobService
            {
                OnListJobs = (request, context) =>
                {
                    ++writes;
                    Assert.Equal("request token", context.RequestHeaders.FirstOrDefault(x => x.Key == "x-csrf-token")?.Value);
                    return Task.FromException<ListJobsResponse>(new RpcException(new Status(StatusCode.Internal, "")));
                }
            },
            new TestExecutionService(),
            cancellationToken: TestContext.Current.CancellationToken
        );

        var client = scope.Services.GetRequiredService<JobApiClient>();

        Assert.False((await client.ListJobsAsync(100, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, writes);
    }
#if false
    [Fact]
    public async Task InvalidDataPayloadIsRecoverable()
    {
        var transport = new BffTransport(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResult<string>.Success("not-json")),
        }))
        { BaseAddress = new Uri("https://localhost") });
        var result = await new JobApiClient(transport).GetJobAsync("id", TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"token\":null}")]
    public async Task MissingAntiforgeryTokenFailsClosed(string json)
    {
        var writes = 0;
        var transport = new BffTransport(new HttpClient(new Handler(request =>
        {
            if (request.Method != HttpMethod.Get)
            {
                writes++;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }))
        { BaseAddress = new Uri("https://localhost") });
        Assert.False((await transport.SendAsync(HttpMethod.Post, "bff/jobs", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(0, writes);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
#endif
}
