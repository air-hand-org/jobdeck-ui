using Grpc.Core;
using Jobdeck.Bff.V1;
using Microsoft.AspNetCore.Antiforgery;

// TODO: fix namespace to ui.bff
namespace ui.Endpoints;

public sealed class BrowserAntiforgeryService(IAntiforgery antiforgery) : AntiforgeryService.AntiforgeryServiceBase
{
    public override async Task<GetAntiforgeryTokenResponse> GetAntiforgeryToken(GetAntiforgeryTokenRequest request, ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        httpContext.Response.Headers.CacheControl = "no-store";

        var tokenset = antiforgery.GetAndStoreTokens(httpContext);

        return new GetAntiforgeryTokenResponse
        {
            RequestToken = tokenset.RequestToken
        };
    }
}
