using System.Net;
using System.Security.Claims;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ui.Bff.Auth;
using ui.Bff.Client;
using ui.Bff.Endpoints;
using ui.Bff.Endpoints.Interceptors;
using ui.Bff.Session;

namespace UI.Tests.Bff;

public sealed class SessionInvalidationTests
{
    [Theory]
    [InlineData("antiforgery")]
    [InlineData("job")]
    [InlineData("execution")]
    public async Task DeletedSessionRejectsPreviouslyAuthenticatedCookieBeforeBusinessInvocation(string endpoint)
    {
        await using var app = await CreateServerAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        var sessions = app.Services.GetRequiredService<IUISessionStore>();
        var backend = app.Services.GetRequiredService<Backend>();
        var sessionId = Guid.NewGuid();
        await sessions.SetSessionAsync(new UISession(sessionId, "server-only-token",
            DateTimeOffset.UtcNow.AddHours(1), new UIUserProfile("test")), cancellationToken);
        using var http = app.GetTestClient();
        using var signedIn = await http.GetAsync($"/test/sign-in/{sessionId}", cancellationToken);
        signedIn.EnsureSuccessStatusCode();
        var cookie = Assert.Single(signedIn.Headers.GetValues("Set-Cookie")).Split(';', 2)[0];
        using var transport = new ResponseObserver(app.GetTestServer().CreateHandler());
        using var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpVersion = HttpVersion.Version11,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, transport)
        });
        var headers = new Metadata { { "cookie", cookie } };
        var antiforgery = new AntiforgeryService.AntiforgeryServiceClient(channel);
        var issued = await antiforgery.GetAntiforgeryTokenAsync(new GetAntiforgeryTokenRequest(), headers,
            cancellationToken: cancellationToken);
        Assert.False(string.IsNullOrEmpty(issued.RequestToken));
        var csrfCookie = Assert.Single(transport.Cookies).Split(';', 2)[0];
        headers = new Metadata
        {
            { "cookie", cookie + "; " + csrfCookie },
            { "x-csrf-token", issued.RequestToken }
        };

        async Task InvokeAsync()
        {
            switch (endpoint)
            {
                case "antiforgery":
                    await antiforgery.GetAntiforgeryTokenAsync(new GetAntiforgeryTokenRequest(), headers,
                        cancellationToken: cancellationToken);
                    break;
                case "job":
                    await new JobService.JobServiceClient(channel).DeleteJobAsync(new DeleteJobRequest { JobId = "job-id" },
                        headers, cancellationToken: cancellationToken);
                    break;
                case "execution":
                    await new ExecutionService.ExecutionServiceClient(channel).DoExecutionAsync(new DoExecutionRequest { JobId = "job-id" },
                        headers, cancellationToken: cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException(endpoint);
            }
        }

        // A successful call proves the same cookie, CSRF token and route work before revocation.
        await InvokeAsync();
        Assert.Equal(HttpStatusCode.OK, transport.StatusCode);
        Assert.Equal(endpoint == "antiforgery" ? 0 : 1, backend.Calls);
        var businessCalls = app.Services.GetRequiredService<BusinessCounter>().Calls;
        Assert.Equal(2, businessCalls);
        await sessions.RemoveSessionAsync(sessionId, cancellationToken);
        Assert.Null(await sessions.GetSessionAsync(sessionId, cancellationToken));

        // Replay the original cookie, including a valid CSRF token, as another open tab would.
        var error = await Assert.ThrowsAsync<RpcException>(InvokeAsync);

        Assert.Equal(StatusCode.Unauthenticated, error.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, transport.StatusCode);
        Assert.Null(transport.Location);
        Assert.Equal(endpoint == "antiforgery" ? 0 : 1, backend.Calls);
        Assert.Equal(businessCalls, app.Services.GetRequiredService<BusinessCounter>().Calls);
    }

    private static async Task<WebApplication> CreateServerAsync()
    {
        // Minimal BFF host: real cookie middleware/events, session store and browser services.
        // Only the remote backend transport is substituted; no synthetic auth failures.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IUISessionStore, MemoryUISessionStore>();
        builder.Services.AddScoped<UICookieAuthenticationEvents>();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.EventsType = typeof(UICookieAuthenticationEvents);
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddSingleton<BusinessCounter>();
        builder.Services.AddGrpc(options => options.Interceptors.Add<BusinessCounter>())
            .AddServiceOptions<BrowserJobService>(options =>
            {
                options.Interceptors.Add<ErrorInterceptor>();
                options.Interceptors.Add<AntiforgeryInterceptor>();
            })
            .AddServiceOptions<BrowserExecutionService>(options =>
            {
                options.Interceptors.Add<ErrorInterceptor>();
                options.Interceptors.Add<AntiforgeryInterceptor>();
            });
        builder.Services.AddSingleton<Backend>();
        builder.Services.AddSingleton(services => new JobService.JobServiceClient(services.GetRequiredService<Backend>()));
        builder.Services.AddSingleton(services => new ExecutionService.ExecutionServiceClient(services.GetRequiredService<Backend>()));
        builder.Services.AddScoped<BackendJobClient>();
        builder.Services.AddScoped<BackendExecutionClient>();
        var app = builder.Build();
        try
        {
            app.UseRouting();
            app.UseGrpcWeb();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapGet("/test/sign-in/{sid:guid}", async (Guid sid, HttpContext context) =>
                await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sid", sid.ToString())], CookieAuthenticationDefaults.AuthenticationScheme))));
            app.MapGrpcService<BrowserAntiforgeryService>().EnableGrpcWeb().RequireAuthorization();
            app.MapGrpcService<BrowserJobService>().EnableGrpcWeb().RequireAuthorization();
            app.MapGrpcService<BrowserExecutionService>().EnableGrpcWeb().RequireAuthorization();
            await app.StartAsync(TestContext.Current.CancellationToken);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private sealed class ResponseObserver(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public HttpStatusCode StatusCode { get; private set; }
        public Uri? Location { get; private set; }
        public string[] Cookies { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            StatusCode = response.StatusCode;
            Location = response.Headers.Location;
            Cookies = response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.ToArray() : [];
            return response;
        }
    }

    private sealed class BusinessCounter : Grpc.Core.Interceptors.Interceptor
    {
        public int Calls { get; private set; }

        public override Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request,
            ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            ++Calls;
            return continuation(request, context);
        }
    }

    private sealed class Backend : CallInvoker
    {
        public int Calls { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method,
            string? host, CallOptions options, TRequest request)
        {
            ++Calls;
            object response = request switch
            {
                DeleteJobRequest => new DeleteJobResponse(),
                DoExecutionRequest => new DoExecutionResponse(),
                _ => throw new InvalidOperationException(method.FullName)
            };
            return new(Task.FromResult((TResponse)response), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
