namespace ui.Session;

public interface IUISessionStore
{
    // return null if session not exist or expired
    Task<UISession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken);
    Task SetSessionAsync(UISession session, CancellationToken cancellationToken);
    Task RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}
