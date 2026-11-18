using Grpc.Core;
using Jobdeck.Bff.V1;
using Microsoft.AspNetCore.Antiforgery;

namespace ui.Bff.Endpoints;

public sealed class BrowserAntiforgeryService(IAntiforgery antiforgery) : AntiforgeryService.AntiforgeryServiceBase
{
    public override async Task<GetAntiforgeryTokenResponse> GetAntiforgeryToken(GetAntiforgeryTokenRequest request, ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();

        var tokenset = antiforgery.GetAndStoreTokens(httpContext);

        return new GetAntiforgeryTokenResponse
        {
            RequestToken = tokenset.RequestToken
        };
    }
}
