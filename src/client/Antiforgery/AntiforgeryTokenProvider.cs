namespace ui.Client.Antiforgery;

public sealed class AntiforgeryTokenProvider(Api.AntiforgeryApiClient antiforgeryClient) : IAntiforgeryTokenProvider
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
                var result = await antiforgeryClient.GetTokenAsync(cancellationToken);
                cache_ = result.IsSuccess ? result.Value!.RequestToken : string.Empty;
            }
            return cache_;
        }
        finally
        {
            semaphore_.Release();
        }
    }
}
