using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ui.Client;
using Grpc.Net.Client.Web;
using Jobdeck.Job.V1;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
if (builder.HostEnvironment.IsProduction())
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}
builder.Services
    .AddGrpcClient<Jobdeck.Bff.V1.AntiforgeryService.AntiforgeryServiceClient>(options =>
    {
        options.Address = new Uri(builder.HostEnvironment.BaseAddress);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(new HttpClientHandler()))
    ;
builder.Services.AddScoped<AntiforgeryApiClient>();
builder.Services.AddScoped<IAntiforgeryTokenProvider, AntiforgeryTokenProvider>();
builder.Services.AddGrpcClient<JobService.JobServiceClient>(options =>
    {
        options.Address = new Uri(builder.HostEnvironment.BaseAddress);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(new HttpClientHandler()))
    .AddCallCredentials(async (context, metadata, services) =>
    {
        var provider = services.GetRequiredService<IAntiforgeryTokenProvider>();
        var token = await provider.GetTokenAsync(context.CancellationToken);
        metadata.Add("x-csrf-token", token);
    })
    ;
builder.Services.AddGrpcClient<ExecutionService.ExecutionServiceClient>(options =>
    {
        options.Address = new Uri(builder.HostEnvironment.BaseAddress);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new GrpcWebHandler(new HttpClientHandler()))
    .AddCallCredentials(async (context, metadata, services) =>
    {
        var provider = services.GetRequiredService<IAntiforgeryTokenProvider>();
        var token = await provider.GetTokenAsync(context.CancellationToken);
        metadata.Add("x-csrf-token", token);
    })
    ;
builder.Services.AddScoped<IJobPageClient, BrowserJobPageClient>();
builder.Services.AddScoped<JobApiClient>();
builder.Services.AddScoped<ExecutionApiClient>();
await builder.Build().RunAsync();
