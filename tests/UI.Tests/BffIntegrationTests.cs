using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Jobdeck.Job.V1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ui.Auth;
using ui.Client;
using ui.Endpoints;
using ui.Models;
using ui.Session;
using HttpMethod = System.Net.Http.HttpMethod;

namespace UI.Tests;

// Real HTTP, cookie middleware, session validation, antiforgery, endpoint binding,
// backend clients and generated protobuf messages; only the remote RPC transport is substituted.
public sealed class BffIntegrationTests
{
    [Fact]
    public async Task AnonymousAndRevokedSessionsCannotReadOrMutate()
    {
        await using var host = await Host.Start();
        using var anonymous = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("bff/jobs?pageSize=100", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("bff/jobs", ValidForm(), TestContext.Current.CancellationToken)).StatusCode);
        using var signedIn = await host.SignIn();
        await host.Sessions.RemoveSessionAsync(host.SessionId, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("bff/antiforgery", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, host.Rpc.Calls);
    }

    [Fact]
    public async Task ExpiredServerSessionRejectsAnOtherwiseValidCookie()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        await host.Sessions.SetSessionAsync(new UISession(host.SessionId, "server-only-token", DateTimeOffset.UtcNow.AddMinutes(-1), new("test")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("bff/jobs?pageSize=100", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(0, host.Rpc.Calls);
    }

    [Theory]
    [InlineData("POST", "bff/jobs")]
    [InlineData("PUT", "bff/jobs/job-id")]
    [InlineData("DELETE", "bff/jobs/job-id")]
    [InlineData("POST", "bff/jobs/job-id/executions")]
    public async Task MutationsRejectMissingAndInvalidAntiforgery(string method, string path)
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        foreach (var token in new[] { "", "forged" })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(ValidForm()) };
            if (token.Length != 0)
            {
                request.Headers.Add("X-CSRF-TOKEN", token);
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode);
        }
        Assert.Equal(0, host.Rpc.Calls);
    }

    [Fact]
    public async Task AuthorizedCrudAndExecutionRoundTripPreservesServerOnlyToken()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        var jobs = new JobApiClient(new BffTransport(client));
        var executions = new ExecutionApiClient(new BffTransport(client));
        var created = await jobs.CreateJobAsync(ValidForm(), TestContext.Current.CancellationToken);
        Assert.True(created.IsSuccess);
        Assert.Equal("job-id", created.Value!.Id);
        Assert.Equal(JobHttpMethod.Post, created.Value.Form.Method);
        Assert.Equal(new[] { 201, 204 }, created.Value.Form.ExpectedStatusCodes);
        Assert.Equal(3, created.Value.Form.MaxAttempts);
        Assert.True((await jobs.UpdateJobAsync("job-id", ValidForm(), TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await jobs.GetJobAsync("job-id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Single((await jobs.ListJobsAsync(100, cancellationToken: TestContext.Current.CancellationToken)).Value!.Jobs);
        Assert.True((await executions.DoExecutionAsync("job-id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Single((await executions.ListExecutionsAsync("job-id", 100, cancellationToken: TestContext.Current.CancellationToken)).Value!.Executions);
        Assert.True((await jobs.DeleteJobAsync("job-id", TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(7, host.Rpc.Calls);
        Assert.Equal("Bearer server-only-token", host.Rpc.Authorization);
        var response = await client.GetAsync("bff/jobs?pageSize=100", TestContext.Current.CancellationToken);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("server-only-token", json);
        Assert.DoesNotContain("owner-id", json);
    }

    [Fact]
    public async Task BackendAuthorizationFailureCannotBeBypassedWithAJobId()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        host.Rpc.Error = new RpcException(new Status(StatusCode.PermissionDenied, "private owner-id"));
        var result = await new JobApiClient(new BffTransport(client)).GetJobAsync("someone-elses-job", TestContext.Current.CancellationToken);
        Assert.Equal(ApiErrorCode.PermissionDenied, result.Error!.Code);
        Assert.DoesNotContain("owner-id", result.Error.Message);
    }

    [Fact]
    public async Task ServerRejectsInvalidFormWithoutCallingBackend()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        var result = await new JobApiClient(new BffTransport(client)).CreateJobAsync(new HttpJobFormModel(), TestContext.Current.CancellationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal(0, host.Rpc.Calls);
    }

    [Fact]
    public async Task LogoutRemovesSessionAndRejectsCopiedCookiesInOtherTabs()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        using var otherTab = host.Client();
        otherTab.DefaultRequestHeaders.Add("Cookie", host.SessionCookie);
        var csrf = await client.GetFromJsonAsync<JsonElement>("bff/antiforgery", TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "test/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode);
        Assert.Null(await host.Sessions.GetSessionAsync(host.SessionId, TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherTab.GetAsync("bff/jobs?pageSize=100", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("bff/jobs?pageSize=100", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task JobPagePrerendersDataAndBootsWasmWithoutACircuit()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        var html = await client.GetStringAsync("jobs", TestContext.Current.CancellationToken);
        Assert.Contains("webassembly", html);
        Assert.Contains("example", html);
        Assert.Contains("Blazor-WebAssembly-Component-State:", html);
        Assert.DoesNotContain("owner-id", html);
        Assert.DoesNotContain("server-only-token", html);
        Assert.DoesNotContain("<!--Blazor:{\"type\":\"server\"", html);
        Assert.Equal(1, host.Rpc.Calls);
        AssertSafePrerenderState(html);
    }

    [Theory]
    [InlineData("jobs/new", "New Job", 0)]
    [InlineData("jobs/job-id", "Job detail", 0)]
    [InlineData("jobs/job-id/edit", "value=\"example\"", 1)]
    [InlineData("jobs/job-id/delete", "example", 1)]
    [InlineData("jobs/job-id/executions", "execution-id", 2)]
    public async Task JobPagesIncludeInitialHtmlBeforeWasmLoads(string path, string content, int reads)
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains(content, html);
        Assert.Contains("webassembly", html);
        Assert.DoesNotContain("server-only-token", html);
        Assert.DoesNotContain("owner-id", html);
        Assert.Equal(reads, host.Rpc.Calls);
        if (reads > 0)
        {
            AssertSafePrerenderState(html);
        }
    }

    [Fact]
    public async Task GrpcWebListsJobsOnTheSameHttp11Port()
    {
        await using var host = await Host.Start();
        using var browser = await host.SignIn();
        using var csrfResponse = await browser.GetAsync("bff/antiforgery", TestContext.Current.CancellationToken);
        var csrf = await csrfResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var cookies = host.SessionCookie + "; " + string.Join("; ", csrfResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        using var channel = GrpcChannel.ForAddress(browser.BaseAddress!, new GrpcChannelOptions
        {
            HttpVersion = HttpVersion.Version11,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler { UseCookies = false }),
        });
        var headers = new Metadata
        {
            { "cookie", cookies },
            { "x-csrf-token", csrf.GetProperty("token").GetString()! },
        };
        var response = await new JobService.JobServiceClient(channel).ListJobsAsync(new ListJobsRequest { PageSize = 100 }, headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(response.Jobs);
        Assert.Equal("Bearer server-only-token", host.Rpc.Authorization);
        Assert.Equal(1, host.Rpc.Calls);
        var unsupported = await Assert.ThrowsAsync<RpcException>(async () => await new JobService.JobServiceClient(channel).DeleteJobAsync(new DeleteJobRequest { JobId = "job-id" }, headers, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unimplemented, unsupported.StatusCode);
        var client = new JobService.JobServiceClient(channel);
        var invalidPage = await Assert.ThrowsAsync<RpcException>(async () => await client.ListJobsAsync(new ListJobsRequest { PageSize = 101 }, headers, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.InvalidArgument, invalidPage.StatusCode);
        var invalidCsrf = new Metadata { { "cookie", cookies }, { "x-csrf-token", "forged" } };
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await client.ListJobsAsync(new ListJobsRequest { PageSize = 100 }, invalidCsrf, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        await host.Sessions.RemoveSessionAsync(host.SessionId, TestContext.Current.CancellationToken);
        var revoked = await Assert.ThrowsAsync<RpcException>(async () => await client.ListJobsAsync(new ListJobsRequest { PageSize = 100 }, headers, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unauthenticated, revoked.StatusCode);
        Assert.Equal(1, host.Rpc.Calls);
    }

    [Theory]
    [InlineData(false, StatusCode.Unauthenticated)]
    [InlineData(true, StatusCode.PermissionDenied)]
    public async Task GrpcWebRejectsMissingSessionOrAntiforgery(bool signedIn, StatusCode expected)
    {
        await using var host = await Host.Start();
        using var browser = signedIn ? await host.SignIn() : host.Client();
        using var channel = GrpcChannel.ForAddress(browser.BaseAddress!, new GrpcChannelOptions
        {
            HttpVersion = HttpVersion.Version11,
            HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
            HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler { UseCookies = false }),
        });
        var headers = new Metadata();
        if (signedIn)
        {
            headers.Add("cookie", host.SessionCookie);
        }
        var error = await Assert.ThrowsAsync<RpcException>(async () => await new JobService.JobServiceClient(channel).ListJobsAsync(new ListJobsRequest { PageSize = 100 }, headers, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(expected, error.StatusCode);
        Assert.Equal(0, host.Rpc.Calls);
    }

    [Fact]
    public async Task RevokedSessionCannotPrerenderJobData()
    {
        await using var host = await Host.Start();
        using var client = await host.SignIn();
        await host.Sessions.RemoveSessionAsync(host.SessionId, TestContext.Current.CancellationToken);
        using var response = await client.GetAsync("jobs", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(0, host.Rpc.Calls);
    }

    private static void AssertSafePrerenderState(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html, "<!--Blazor-WebAssembly-Component-State:([A-Za-z0-9+/=]+)-->");
        Assert.True(match.Success);
        var state = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value));
        Assert.DoesNotContain("server-only-token", state);
        Assert.DoesNotContain("owner-id", state);
        Assert.DoesNotContain("Authorization", state);
    }

    private static HttpJobFormModel ValidForm() => new()
    {
        Name = "example",
        Url = "https://example.com/run",
        Method = JobHttpMethod.Post,
        ExpectedStatusCodes = [201, 204],
        TimeoutSeconds = 30,
        FollowRedirects = true,
        RetryOnTimeout = true,
        RetryOnNetworkError = true,
        RetryOnStatusCodes = [503],
        MaxAttempts = 3,
        InitialDelaySeconds = 2,
        MaxDelaySeconds = 5,
    };

    private sealed class Host(WebApplication app, Uri address, IUISessionStore sessions, Rpc rpc) : IAsyncDisposable
    {
        public IUISessionStore Sessions => sessions;
        public Rpc Rpc => rpc;
        public Guid SessionId { get; } = Guid.NewGuid();
        public string SessionCookie { get; private set; } = "";
        public HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
        public async Task<HttpClient> SignIn()
        {
            await sessions.SetSessionAsync(new UISession(SessionId, "server-only-token", DateTimeOffset.UtcNow.AddHours(1), new("test")), TestContext.Current.CancellationToken);
            var client = Client();
            var response = await client.GetAsync($"test/sign-in/{SessionId}", TestContext.Current.CancellationToken);
            SessionCookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
            return client;
        }
        public static async Task<Host> Start()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(ui.Components.App).Assembly.GetName().Name });
            builder.WebHost.UseStaticWebAssets();
            builder.Services.AddGrpc();
            builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<ui.Profile.UIUserProfileService>();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IUISessionStore, MemoryUISessionStore>();
            builder.Services.AddScoped<UICookieAuthenticationEvents>();
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => options.EventsType = typeof(UICookieAuthenticationEvents));
            builder.Services.AddAuthorization();
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<AccessTokenAccessor>();
            var rpc = new Rpc();
            builder.Services.AddSingleton(new JobService.JobServiceClient(rpc));
            builder.Services.AddSingleton(new ExecutionService.ExecutionServiceClient(rpc));
            builder.Services.AddScoped<BackendJobClient>();
            builder.Services.AddScoped<IJobPageClient, PrerenderJobPageClient>();
            builder.Services.AddScoped<BackendExecutionClient>();
            builder.Services.AddSingleton(new Jobdeck.Auth.V1.AuthService.AuthServiceClient(rpc));
            builder.Services.AddScoped<AuthApiClient>();
            builder.Services.AddScoped<UIAuthenticationService>();
            var app = builder.Build();
            app.UseRouting();
            app.UseGrpcWeb();
            app.UsePrivateJobPages();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapStaticAssets().ShortCircuit();
            app.MapRazorComponents<ui.Components.App>().AddInteractiveWebAssemblyRenderMode().AddAdditionalAssemblies(typeof(JobApiClient).Assembly);
            app.MapBffEndpoints();
            app.MapGrpcService<BrowserJobService>().EnableGrpcWeb().RequireAuthorization();
            app.MapPost("/test/logout", async (Microsoft.AspNetCore.Http.HttpContext context, UIAuthenticationService auth) =>
            {
                await auth.LogoutAsync(context, context.RequestAborted);
                return Microsoft.AspNetCore.Http.Results.Ok();
            }).RequireAuthorization().AddEndpointFilter<BffRequestFilter>();
            app.MapGet("/test/sign-in/{sid}", async (string sid, Microsoft.AspNetCore.Http.HttpContext context) =>
                await context.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sid", sid)], CookieAuthenticationDefaults.AuthenticationScheme))));
            await app.StartAsync(TestContext.Current.CancellationToken);
            return new(app, new Uri(app.Urls.Single()), app.Services.GetRequiredService<IUISessionStore>(), rpc);
        }
        public async ValueTask DisposeAsync() { await app.StopAsync(TestContext.Current.CancellationToken); await app.DisposeAsync(); }
    }

    private sealed class Rpc : CallInvoker
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        public Exception? Error { get; set; }
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Calls++;
            Authorization = options.Headers?.GetValue("authorization");
            var job = new Job { Id = "job-id", OwnerId = "owner-id", Name = "example", Enabled = true, Spec = ValidForm().ToJobSpec() };
            object response = request switch
            {
                CreateJobRequest create => new CreateJobResponse { Job = new Job { Id = job.Id, Name = create.Name, Spec = create.Spec } },
                UpdateJobRequest update => new UpdateJobResponse { Job = new Job { Id = update.JobId, Name = update.Name, Spec = update.Spec } },
                GetJobRequest => new GetJobResponse { Job = job },
                ListJobsRequest => new ListJobsResponse { Jobs = { job } },
                DeleteJobRequest => new DeleteJobResponse(),
                DoExecutionRequest => new DoExecutionResponse(),
                ListExecutionsRequest => new ListExecutionsResponse { Executions = { new Execution { Id = "execution-id", Attempt = 1 } } },
                _ => throw new InvalidOperationException(),
            };
            return new(Error is null ? Task.FromResult((TResponse)response) : Task.FromException<TResponse>(Error), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
