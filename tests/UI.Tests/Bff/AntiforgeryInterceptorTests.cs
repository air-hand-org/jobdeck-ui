using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Jobdeck.Job.V1;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ui.Bff.Endpoints.Interceptors;
using UI.Tests.Helpers;

namespace UI.Tests.Bff;

public sealed class AntiforgeryInterceptorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("invalid-token")]
    [InlineData(null)]
    public async Task InvalidTokenIsRejectedBeforeMutation(string? token)
    {
        await using var server = await CreateServerAsync();
        using var http = server.GetTestClient();
        // Supply a real cookie so rejection exercises the request token, not a missing cookie.
        using var issued = await http.GetAsync("/token", TestContext.Current.CancellationToken);
        var headers = new Metadata { { "cookie", Cookie(issued) } };
        if (token is not null)
        {
            headers.Add("x-csrf-token", token);
        }
        using var channel = CreateChannel(server);
        var client = new JobService.JobServiceClient(channel);

        var error = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.DeleteJobAsync(new DeleteJobRequest { JobId = "id" }, headers,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Equal("CSRF validation failed", error.Status.Detail);
        Assert.Equal(0, server.Services.GetRequiredService<MutationCounter>().Writes);
    }

    [Fact]
    public async Task ValidRequestTokenAndCookieAllowMutation()
    {
        await using var server = await CreateServerAsync();
        using var http = server.GetTestClient();
        using var issued = await http.GetAsync("/token", TestContext.Current.CancellationToken);
        issued.EnsureSuccessStatusCode();
        var token = await issued.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrEmpty(token));
        var headers = new Metadata { { "cookie", Cookie(issued) }, { "x-csrf-token", token } };
        using var channel = CreateChannel(server);
        var client = new JobService.JobServiceClient(channel);

        await client.DeleteJobAsync(new DeleteJobRequest { JobId = "id" }, headers,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, server.Services.GetRequiredService<MutationCounter>().Writes);
    }

    private static string Cookie(HttpResponseMessage response)
        => Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];

    private static GrpcChannel CreateChannel(WebApplication app)
        => GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, app.GetTestServer().CreateHandler())
        });

    private static async Task<WebApplication> CreateServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddGrpc().AddServiceOptions<TestJobService>(options =>
            options.Interceptors.Add<AntiforgeryInterceptor>());
        var counter = new MutationCounter();
        builder.Services.AddSingleton(counter);
        builder.Services.AddSingleton(new TestJobService
        {
            OnDeleteJob = (_, _) =>
            {
                ++counter.Writes;
                return Task.FromResult(new DeleteJobResponse());
            }
        });
        var app = builder.Build();
        try
        {
            app.UseRouting();
            app.UseGrpcWeb();
            app.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) =>
                antiforgery.GetAndStoreTokens(context).RequestToken!);
            app.MapGrpcService<TestJobService>().EnableGrpcWeb();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class MutationCounter
    {
        public int Writes;
    }
}
