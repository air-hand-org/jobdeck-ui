using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ui.Client;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Jobdeck.Job.V1;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(_ => GrpcChannel.ForAddress(builder.HostEnvironment.BaseAddress, new GrpcChannelOptions
{
    HttpVersion = System.Net.HttpVersion.Version11,
    HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact,
    HttpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new HttpClientHandler()),
}));
builder.Services.AddScoped(provider => new JobService.JobServiceClient(provider.GetRequiredService<GrpcChannel>()));
builder.Services.AddScoped<BrowserJobClient>();
builder.Services.AddScoped<BffTransport>();
builder.Services.AddScoped<JobApiClient>();
builder.Services.AddScoped<ExecutionApiClient>();
await builder.Build().RunAsync();
