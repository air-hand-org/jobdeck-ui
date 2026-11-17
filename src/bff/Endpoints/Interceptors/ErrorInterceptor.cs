using Grpc.Core;
using Grpc.Core.Interceptors;
using ui.Client;

namespace ui.Endpoints.Interceptors;

public sealed class ErrorInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request
        , ServerCallContext context
        , UnaryServerMethod<TRequest, TResponse> next
    )
    {
        try
        {
            return await next(request, context);
        }
        catch (RpcException ex)
        {
            var error = BackendErrors.FromRPCException(ex);
            throw new RpcException(new Status(ex.StatusCode, error.Message));
        }
    }
}
