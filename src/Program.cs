using System.Diagnostics;
using ui.Auth;
using ui.Components;
using ui.Client;
using ui.Profile;
using ui.Session;
using ui.Telemetry;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Jobdeck.Auth.V1;
using Jobdeck.Job.V1;
using Grpc.Health.V1;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ui.Endpoints;
using ui.DB;
using Microsoft.EntityFrameworkCore;
using ui.Certificates;
using Quartz;
using Quartz.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddRazorComponents()
  .AddInteractiveServerComponents()
  ;
builder.Services.AddOpenTelemetry()
  .ConfigureResource(resource => resource.AddService("jobdeck-ui"))
  .WithTracing(tracing =>
  {
      tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource(QuartzInstrumentation.ActivitySourceName)
        .AddSource(UiTelemetry.ActivitySourceName)
        ;

      if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
      {
          tracing.AddOtlpExporter();
      }
  })
  .WithMetrics(metrics =>
  {
      metrics.AddMeter(QuartzInstrumentation.MeterName);

      if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
      {
          metrics.AddOtlpExporter();
      }
  })
  ;
builder.Services.AddGrpcClient<JobService.JobServiceClient>(options =>
  {
      var address = builder.Configuration["JOB_API_GRPC_ENDPOINT"] ?? throw new InvalidOperationException("JOB_API_GRPC_ENDPOINT is required");
      options.Address = new Uri(address);
  }
);
builder.Services.AddGrpcClient<ExecutionService.ExecutionServiceClient>(options =>
  {
      var address = builder.Configuration["JOB_API_GRPC_ENDPOINT"] ?? throw new InvalidOperationException("JOB_API_GRPC_ENDPOINT is required");
      options.Address = new Uri(address);
  }
);
builder.Services.AddGrpcClient<AuthService.AuthServiceClient>(options =>
  {
      var address = builder.Configuration["AUTH_GRPC_ENDPOINT"] ?? throw new InvalidOperationException("AUTH_GRPC_ENDPOINT is required");
      options.Address = new Uri(address);
  }
);
builder.Services.AddScoped<JobApiClient>();
builder.Services.AddScoped<ExecutionApiClient>();
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<UICookieAuthenticationEvents>();

builder.Services.AddGrpcClient<Health.HealthClient>(
    "job-api-health",
    options =>
    {
        var address = builder.Configuration["JOB_API_GRPC_ENDPOINT"] ?? throw new InvalidOperationException("JOB_API_GRPC_ENDPOINT is required");
        options.Address = new Uri(address);
    }
);
builder.Services.AddGrpcClient<Health.HealthClient>(
    "auth-health",
    options =>
    {
        var address = builder.Configuration["AUTH_GRPC_ENDPOINT"] ?? throw new InvalidOperationException("AUTH_GRPC_ENDPOINT is required");
        options.Address = new Uri(address);
    }
);

builder.Services.AddPooledDbContextFactory<ApplicationDbContext>(options =>
{
    options.UseNpgsql(PostgresConfiguration.GetConnectionString(builder.Configuration));

    options.UseOpenIddict();
});

builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<HealthCheckClient>(
        "job-api",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: ["job-api-health"]
    )
    .AddTypeActivatedCheck<HealthCheckClient>(
        "auth",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        args: ["auth-health"]
    )
    .AddDbContextCheck<ApplicationDbContext>(
        "db",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"]
    )
    ;

builder.Services.AddDataProtection()
    .SetApplicationName("jobdeck-ui")
    .PersistKeysToDbContext<ApplicationDbContext>()
    .ProtectKeysWithCertificate(
        CertLoader.LoadFromBase64Env(builder.Configuration, "CERT_DATAPROTECTION_BASE64")
    )
    ;

builder.Services.AddQuartz(options =>
{
    options.UseInMemoryStore();
    options.AddDeclaredJobs();
});
builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<ApplicationDbContext>();
        options.UseQuartz();
    })
    .AddClient(options =>
    {
        options.AddEncryptionCertificate(
            CertLoader.LoadFromBase64Env(builder.Configuration, "CERT_OPENID_ENCRYPTION_BASE64")
        ).AddSigningCertificate(
            CertLoader.LoadFromBase64Env(builder.Configuration, "CERT_OPENID_SIGNING_BASE64")
        );

        options.AllowAuthorizationCodeFlow();

        options.UseAspNetCore()
            .EnableRedirectionEndpointPassthrough()
            .EnablePostLogoutRedirectionEndpointPassthrough()
            ;

        options.UseSystemNetHttp();

        options.UseWebProviders()
            .AddGitHub(options =>
            {
                var clientID = builder.Configuration["GITHUB_OAUTH_CLIENT_ID"] ?? throw new InvalidOperationException("GITHUB_OAUTH_CLIENT_ID is required");
                var clientSecret = builder.Configuration["GITHUB_OAUTH_CLIENT_SECRET"] ?? throw new InvalidOperationException("GITHUB_OAUTH_CLIENT_SECRET is required");
                options.SetClientId(clientID)
                    .SetClientSecret(clientSecret)
                    .SetRedirectUri("account/github/callback/login")
                    ;
            })
        ;
    })
    ;

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
        {
            options.EventsType = typeof(UICookieAuthenticationEvents);
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = false;
        }
    );
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<IUISessionStore, PostgresUISessionStore>();
builder.Services.AddScoped<AccessTokenAccessor>();
builder.Services.AddScoped<UIAuthenticationService>();
builder.Services.AddScoped<UIUserProfileService>();

if (builder.Configuration.GetValue<bool>("MIGRATOR_EXECUTE"))
{
    using var migrator = Process.Start(new ProcessStartInfo
    {
        FileName = "/app/migrator",
        WorkingDirectory = "/app",
        UseShellExecute = false,
    }) ?? throw new InvalidOperationException("Failed to start migrator.");

    await migrator.WaitForExitAsync();

    if (migrator.ExitCode != 0)
    {
        throw new InvalidOperationException($"Migrator failed with exit code {migrator.ExitCode}");
    }
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks(
    "/health/live",
    new HealthCheckOptions
    {
        Predicate = _ => false,
    }
);
app.MapHealthChecks(
    "/health/ready",
    new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
    }
);
app.MapStaticAssets().ShortCircuit();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGitHubAuthenticationEndpoints();

app.Run();
