namespace ui.Client;

public interface IAntiforgeryTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken);
}
