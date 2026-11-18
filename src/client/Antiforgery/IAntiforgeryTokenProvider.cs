namespace ui.Client.Antiforgery;

public interface IAntiforgeryTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
