using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ui.Client.Extensions;

namespace UI.Tests.Helpers;

public sealed class TestServerScope : IAsyncDisposable
{
    private readonly WebApplication app_;
    private readonly ServiceProvider provider_;
    private readonly AsyncServiceScope scope_;

    private TestServerScope(
        WebApplication app,
        ServiceProvider provider,
        AsyncServiceScope scope
    )
    {
        app_ = app;
        provider_ = provider;
        scope_ = scope;
    }

    public IServiceProvider Services => scope_.ServiceProvider;

    public static async Task<TestServerScope> CreateAsync(
        TestAntiforgeryService antiforgery,
        TestJobService job,
        TestExecutionService execution,
        CancellationToken cancellationToken = default,
        bool validateAntiforgery = false
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var grpc = builder.Services.AddGrpc();
        // Enable the real interceptor for the empty-token rejection test; other tests inspect synthetic tokens.
        if (validateAntiforgery)
        {
            builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
            grpc.AddServiceOptions<TestJobService>(options => options.Interceptors.Add<ui.Bff.Endpoints.Interceptors.AntiforgeryInterceptor>());
        }
        builder.Services.AddSingleton(antiforgery);
        builder.Services.AddSingleton(job);
        builder.Services.AddSingleton(execution);

        var app = builder.Build();
        ServiceProvider? provider = null;
        AsyncServiceScope? scope = null;

        try
        {
            app.UseRouting();
            app.UseGrpcWeb();

            app.MapGrpcService<TestAntiforgeryService>().EnableGrpcWeb();
            app.MapGrpcService<TestJobService>().EnableGrpcWeb();
            app.MapGrpcService<TestExecutionService>().EnableGrpcWeb();

            await app.StartAsync(cancellationToken);

            var server = app.GetTestServer();

            var services = new ServiceCollection();
            services.AddLogging();

            services.AddBrowserGrpcClients(
                new Uri("https://localhost"),
                createTransport: () => server.CreateHandler()
            );

            provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                }
            );

            scope = provider.CreateAsyncScope();

            return new TestServerScope(app, provider, scope.Value);
        }
        catch
        {
            if (scope is { } s)
            {
                await s.DisposeAsync();
            }
            if (provider is not null)
            {
                await provider.DisposeAsync();
            }
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await scope_.DisposeAsync();
        await provider_.DisposeAsync();
        await app_.DisposeAsync();
    }
}
