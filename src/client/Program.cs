using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Grpc.Net.Client.Web;
using Jobdeck.Job.V1;
using ui.Client.Antiforgery;
using ui.Client.Api;
using ui.Client.Extensions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
if (builder.HostEnvironment.IsProduction())
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}
builder.Services.AddBrowserGrpcClients(new Uri(builder.HostEnvironment.BaseAddress));
builder.Services.AddScoped<IJobPageClient, BrowserJobPageClient>();
await builder.Build().RunAsync();
