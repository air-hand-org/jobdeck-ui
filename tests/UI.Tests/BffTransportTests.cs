using System.Net;
using System.Net.Http.Json;
using ui.Client;

namespace UI.Tests;

public sealed class BffTransportTests
{
    [Fact]
    public async Task UnauthorizedIsAnExpiryErrorNotAnHtmlParseFailure()
    {
        var transport = new BffTransport(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))) { BaseAddress = new Uri("https://localhost") });
        var result = await transport.SendAsync(HttpMethod.Get, "bff/jobs", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ApiErrorCode.Unauthenticated, result.Error!.Code);
    }

    [Fact]
    public async Task NetworkFailureIsRecoverableAndTheNextReadSucceeds()
    {
        var calls = 0;
        var transport = new BffTransport(new HttpClient(new Handler(_ => ++calls == 1
            ? throw new HttpRequestException("offline")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(ApiResult<string>.Success("{}")) }))
        { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(ApiErrorCode.Internal, (await transport.SendAsync(HttpMethod.Get, "bff/jobs", cancellationToken: TestContext.Current.CancellationToken)).Error!.Code);
        Assert.True((await transport.SendAsync(HttpMethod.Get, "bff/jobs", cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MutationsSendAntiforgeryTokenAndAreNotRetried()
    {
        var writes = 0;
        var transport = new BffTransport(new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath == "/bff/antiforgery")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token = "test-antiforgery" }) };
            }
            Assert.Equal("test-antiforgery", request.Headers.GetValues("X-CSRF-TOKEN").Single());
            writes++;
            throw new HttpRequestException("response lost");
        }))
        { BaseAddress = new Uri("https://localhost") });
        Assert.False((await transport.SendAsync(HttpMethod.Post, "bff/jobs", new { name = "job" }, TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(1, writes);
    }

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
}
