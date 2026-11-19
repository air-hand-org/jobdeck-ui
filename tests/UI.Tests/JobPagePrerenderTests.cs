using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ui.Bff.Components;
using ui.Client.Api;
using ui.Client.Models;
using JobIndex = ui.Client.Components.Pages.JobPages.Index;

namespace UI.Tests;

public sealed class JobPagePrerenderTests
{
    [Theory]
    [InlineData("/jobs", true)]
    [InlineData("/jobs/new", false)]
    [InlineData("/jobs/job-id", false)]
    [InlineData("/jobs/job-id/edit", false)]
    [InlineData("/jobs/job-id/delete", false)]
    [InlineData("/jobs/job-id/executions", false)]
    public async Task OnlyJobsListRendersDataOnTheServer(string path, bool prerender)
    {
        var client = new PageClient();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
        builder.Services.AddAuthorization();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddSingleton<IJobPageClient>(client);
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-user")], "test"));
            await next(context);
        });
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapRazorComponents<App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(JobIndex).Assembly);
        await app.StartAsync(TestContext.Current.CancellationToken);

        var response = await app.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("\"type\":\"webassembly\"", html);
        Assert.Equal(prerender, html.Contains("prerenderId", StringComparison.Ordinal));
        Assert.Equal(prerender, html.Contains("Jobs list", StringComparison.Ordinal));
        Assert.Equal(prerender, html.Contains("server-list-job", StringComparison.Ordinal));
        Assert.DoesNotContain("UpdateJob", html);
        Assert.DoesNotContain("Are you sure", html);
        Assert.DoesNotContain("Request to run the job", html);
        Assert.DoesNotContain("New Job", html);
        Assert.DoesNotContain("Job detail", html);
        Assert.Equal(prerender ? 1 : 0, client.ListCalls);
        Assert.Equal(0, client.OtherCalls);
    }

    private sealed class PageClient : IJobPageClient
    {
        public int ListCalls { get; private set; }
        public int OtherCalls { get; private set; }

        public Task<ApiResult<ListJobsResult>> ListJobsAsync(int pageSize, string pageToken = "", CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return Task.FromResult(ApiResult<ListJobsResult>.Success(new ListJobsResult(
                [new JobView("job-id", "server-list-job", true, new HttpJobFormModel())], "")));
        }

        private Task<T> Unexpected<T>()
        {
            OtherCalls++;
            throw new InvalidOperationException("Non-list pages must not read or mutate data during SSR.");
        }

        public Task<ApiResult<JobView>> GetJobAsync(string jobId, CancellationToken cancellationToken = default) => Unexpected<ApiResult<JobView>>();
        public Task<ApiResult<ListExecutionsResult>> ListExecutionsAsync(string jobId, int pageSize, string pageToken = "", CancellationToken cancellationToken = default) => Unexpected<ApiResult<ListExecutionsResult>>();
        public Task<ApiResult<JobView>> CreateJobAsync(HttpJobFormModel model, CancellationToken cancellationToken = default) => Unexpected<ApiResult<JobView>>();
        public Task<ApiResult<JobView>> UpdateJobAsync(string jobId, HttpJobFormModel model, CancellationToken cancellationToken = default) => Unexpected<ApiResult<JobView>>();
        public Task<ApiResult> DeleteJobAsync(string jobId, CancellationToken cancellationToken = default) => Unexpected<ApiResult>();
        public Task<ApiResult> DoExecutionAsync(string jobId, CancellationToken cancellationToken = default) => Unexpected<ApiResult>();
    }
}
