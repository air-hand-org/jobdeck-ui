using Grpc.Net.Client.Web;
using Jobdeck.Bff.V1;
using Jobdeck.Job.V1;
using ui.Client.Antiforgery;
using ui.Client.Api;

namespace ui.Client.Extensions;

public static class BrowserGrpcServiceExtensions
{
    public static IServiceCollection AddBrowserGrpcClients(
        this IServiceCollection services,
        Uri baseAddress,
        Func<HttpMessageHandler>? createTransport = null
    )
    {
        HttpMessageHandler createHandler()
        {
            return new GrpcWebHandler(createTransport?.Invoke() ?? new HttpClientHandler());
        }

        services
            .AddGrpcClient<AntiforgeryService.AntiforgeryServiceClient>(
                options => options.Address = baseAddress
            )
            .ConfigurePrimaryHttpMessageHandler(createHandler);
        ;
        services
            .AddScoped<IAntiforgeryTokenProvider, AntiforgeryTokenProvider>()
        ;
        services
            .AddGrpcClient<JobService.JobServiceClient>(
                options => options.Address = baseAddress
            )
            .ConfigurePrimaryHttpMessageHandler(createHandler)
            .AddCallCredentials(async (context, metadata, services) =>
            {
                var provider = services.GetRequiredService<IAntiforgeryTokenProvider>();
                var token = await provider.GetTokenAsync(context.CancellationToken);
                metadata.Add("x-csrf-token", token);
            })
        ;
        services
            .AddGrpcClient<ExecutionService.ExecutionServiceClient>(
                options => options.Address = baseAddress
            )
            .ConfigurePrimaryHttpMessageHandler(createHandler)
            .AddCallCredentials(async (context, metadata, services) =>
            {
                var provider = services.GetRequiredService<IAntiforgeryTokenProvider>();
                var token = await provider.GetTokenAsync(context.CancellationToken);
                metadata.Add("x-csrf-token", token);
            })
        ;
        services.AddScoped<JobApiClient>();
        services.AddScoped<ExecutionApiClient>();
        return services;
    }
}
