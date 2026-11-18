using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Antiforgery;

namespace ui.Bff.Endpoints.Interceptors;

public sealed class AntiforgeryInterceptor(IAntiforgery antiforgery) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request
        , ServerCallContext context
        , UnaryServerMethod<TRequest, TResponse> next
    )
    {
        try
        {
            await antiforgery.ValidateRequestAsync(
                context.GetHttpContext()
            );
        }
        catch (AntiforgeryValidationException)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied
                , "CSRF validation failed"
            ));
        }
        return await next(request, context);
    }
}
