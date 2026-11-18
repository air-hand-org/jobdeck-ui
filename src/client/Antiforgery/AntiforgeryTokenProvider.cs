using Jobdeck.Bff.V1;

namespace ui.Client.Antiforgery;

public sealed class AntiforgeryTokenProvider(AntiforgeryService.AntiforgeryServiceClient client) : IAntiforgeryTokenProvider
{
    private readonly SemaphoreSlim semaphore_ = new(1, 1);
    private string cache_ = string.Empty;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await semaphore_.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrEmpty(cache_))
            {
                var result = await client.GetAntiforgeryTokenAsync(
                    new GetAntiforgeryTokenRequest(),
                    cancellationToken: cancellationToken
                );
                cache_ = result.RequestToken;
            }
            return cache_;
        }
        finally
        {
            semaphore_.Release();
        }
    }
}
