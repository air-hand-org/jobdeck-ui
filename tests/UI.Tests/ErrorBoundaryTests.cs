using Grpc.Core;
using ui.Client;

namespace UI.Tests;

public sealed class ErrorBoundaryTests
{
    [Fact]
    public void InternalRpcDetailsNeverReachTheBrowser()
    {
        var result = BackendErrors.FromRPCException(new RpcException(new Status(StatusCode.Internal, "private connection string")));
        Assert.DoesNotContain("private", result.Message);
    }
}
