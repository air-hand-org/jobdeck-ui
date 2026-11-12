using Microsoft.Extensions.Caching.Memory;

namespace ui.Session;

public sealed class MemoryUISessionStore(
    IMemoryCache cache
    , TimeProvider timeProvider
) : IUISessionStore
{
    private static string Key(Guid id) => $"session:{id}";

    public async Task<UISession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cache.TryGetValue(Key(sessionId), out UISession? session);
        if (session is null)
        {
            return null;
        }
        if (session.IsExpired(timeProvider.GetUtcNow()))
        {
            await RemoveSessionAsync(sessionId, cancellationToken);
            return null;
        }
        return session;
    }

    public Task SetSessionAsync(UISession session, CancellationToken cancellationToken = default)
    {
        cache.Set(
            Key(session.SessionId),
            session,
            new MemoryCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromHours(8),
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1),
            }
        );
        return Task.CompletedTask;
    }

    public Task RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cache.Remove(Key(sessionId));
        return Task.CompletedTask;
    }
}
