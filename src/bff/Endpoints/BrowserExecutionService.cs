using Grpc.Core;
using Jobdeck.Job.V1;
using ui.Client;

namespace ui.Endpoints;

public sealed class BrowserExecutionService(BackendExecutionClient client) : ExecutionService.ExecutionServiceBase
{
    public override async Task<DoExecutionResponse> DoExecution(DoExecutionRequest request, ServerCallContext context) => await client.DoExecutionAsync(request, context.CancellationToken);
    public override async Task<ListExecutionsResponse> ListExecutions(ListExecutionsRequest request, ServerCallContext context) => await client.ListExecutionsAsync(request, context.CancellationToken);
}
