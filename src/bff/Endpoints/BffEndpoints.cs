using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Google.Protobuf;
using Jobdeck.Job.V1;
using Microsoft.AspNetCore.Antiforgery;
using ui.Client;
using ui.Models;

namespace ui.Endpoints;

public static class BffEndpoints
{
    public static void MapBffEndpoints(this WebApplication app)
    {
        var bff = app.MapGroup("/bff").RequireAuthorization();
        bff.AddEndpointFilter<BffRequestFilter>();
        bff.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        bff.MapGet("/jobs", async (int pageSize, string? pageToken, BackendJobClient jobs, CancellationToken ct) =>
        {
            var result = await jobs.ListJobsAsync(Math.Clamp(pageSize, 1, 100), pageToken ?? "", ct);
            return Data(result, value => new ListJobsResult(value.Jobs.Select(Job).ToList(), value.NextPageToken));
        });
        bff.MapGet("/jobs/{jobId}", async (string jobId, BackendJobClient jobs, CancellationToken ct) => Data(await jobs.GetJobAsync(jobId, ct), Job));
        bff.MapPost("/jobs", async (HttpJobFormModel model, BackendJobClient jobs, CancellationToken ct) =>
            Valid(model) ? Data(await jobs.CreateJobAsync(model, ct), Job) : Results.BadRequest());
        bff.MapPut("/jobs/{jobId}", async (string jobId, HttpJobFormModel model, BackendJobClient jobs, CancellationToken ct) =>
            Valid(model) ? Data(await jobs.UpdateJobAsync(jobId, model, ct), Job) : Results.BadRequest());
        bff.MapDelete("/jobs/{jobId}", async (string jobId, BackendJobClient jobs, CancellationToken ct) => Empty(await jobs.DeleteJobAsync(jobId, ct)));
        bff.MapPost("/jobs/{jobId}/executions", async (string jobId, BackendExecutionClient executions, CancellationToken ct) => Empty(await executions.DoExecutionAsync(jobId, ct)));
        bff.MapGet("/jobs/{jobId}/executions", async (string jobId, int pageSize, string? pageToken, BackendExecutionClient executions, CancellationToken ct) =>
        {
            var result = await executions.ListExecutionsAsync(jobId, Math.Clamp(pageSize, 1, 100), pageToken ?? "", ct);
            return Data(result, value => new ListExecutionsResult(value.Executions.Select(execution => new ExecutionView(
                execution.Id, execution.Attempt, execution.Status.ToString(), execution.CreatedAt?.ToDateTime(), execution.UpdatedAt?.ToDateTime(),
                execution.Result is null ? "-" : JsonFormatter.Default.Format(execution.Result))).ToList(), value.NextPageToken));
        });
    }

    private static JobView Job(Job job) => new(job.Id, job.Name, job.Enabled, JobMapping.FromProto(job));
    private static bool Valid(HttpJobFormModel model) => Validator.TryValidateObject(model, new ValidationContext(model), [], validateAllProperties: true);
    private static IResult Data<T, TView>(ApiResult<T> result, Func<T, TView> map) => Results.Ok(result.IsSuccess
        ? ApiResult<string>.Success(JsonSerializer.Serialize(map(result.Value!)))
        : ApiResult<string>.Failure(result.Error!));
    private static IResult Empty(ApiResult result) => Results.Ok(result.IsSuccess ? ApiResult<string>.Success("") : ApiResult<string>.Failure(result.Error!));
}

public sealed class BffRequestFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden);
            }
        }
        return await next(context);
    }
}
