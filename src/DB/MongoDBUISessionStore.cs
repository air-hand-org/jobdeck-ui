using MongoDB.Driver;
using ui.Session;

namespace ui.DB;

public sealed class MongoDBUISessionStore(MongoDBCollections collections) : IUISessionStore
{
    public async Task<UISession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = collections.Sessions;
        var result = await sessions.Find(
            Builders<MongoDBUISession>.Filter.Eq(x => x.SessionId, sessionId)
        )
        .FirstOrDefaultAsync(cancellationToken)
        ;
        return result?.ToDomain();
    }

    public async Task SetSessionAsync(UISession session, CancellationToken cancellationToken = default)
    {
        var sessions = collections.Sessions;
        await sessions.ReplaceOneAsync(
            Builders<MongoDBUISession>.Filter.Eq(x => x.SessionId, session.SessionId),
            MongoDBUISession.FromDomain(session),
            new ReplaceOptions { IsUpsert = true },
            cancellationToken: cancellationToken
        );
    }

    public async Task RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = collections.Sessions;
        await sessions.DeleteOneAsync(
            Builders<MongoDBUISession>.Filter.Eq(x => x.SessionId, sessionId),
            cancellationToken: cancellationToken
        );
    }
}
