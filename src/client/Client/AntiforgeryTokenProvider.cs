using Jobdeck.Bff.V1;

namespace ui.Client;

public sealed class AntiforgeryTokenProvider(AntiforgeryApiClient antiforgeryClient) : IAntiforgeryTokenProvider
{
    private string cache_ = string.Empty;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(cache_))
        {
            var result = await antiforgeryClient.GetTokenAsync(cancellationToken);
            cache_ = result.IsSuccess ? result.Value!.RequestToken : string.Empty;
        }
        return cache_;
    }
}
