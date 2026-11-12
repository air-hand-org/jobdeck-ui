using Grpc.Core;
using Jobdeck.Job.V1;
using Microsoft.AspNetCore.Antiforgery;
using ui.Client;

namespace ui.Endpoints;

// Only ListJobs is implemented in this trial. Other generated RPCs remain UNIMPLEMENTED.
public sealed class BrowserJobService(BackendJobClient jobs, IAntiforgery antiforgery) : JobService.JobServiceBase
{
    public override async Task<ListJobsResponse> ListJobs(ListJobsRequest request, ServerCallContext context)
    {
        var http = context.GetHttpContext();
        http.Response.Headers.CacheControl = "no-store";
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "再読み込みしてください。"));
        }
        if (request.PageSize is < 1 or > 100)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Page size must be between 1 and 100."));
        }
        var result = await jobs.ListJobsAsync(request.PageSize, request.PageToken, context.CancellationToken);
        if (!result.IsSuccess)
        {
            var code = result.Error!.Code switch
            {
                ApiErrorCode.Unauthenticated => StatusCode.Unauthenticated,
                ApiErrorCode.PermissionDenied => StatusCode.PermissionDenied,
                _ => StatusCode.Unavailable,
            };
            throw new RpcException(new Status(code, result.Error.Message));
        }
        var response = new ListJobsResponse { NextPageToken = result.Value!.NextPageToken };
        response.Jobs.AddRange(result.Value.Jobs);
        return response;
    }
}
