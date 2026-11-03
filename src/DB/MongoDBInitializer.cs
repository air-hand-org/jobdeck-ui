using MongoDB.Driver;

namespace ui.DB;

public interface IInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class MongoDBInitializer(MongoDBCollections collections) : IInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeSessionsAsync(cancellationToken);
    }

    private async Task InitializeSessionsAsync(CancellationToken cancellationToken)
    {
        var sessions = collections.Sessions;
        await sessions.Indexes.CreateOneAsync(
            model: new CreateIndexModel<MongoDBUISession>(
                keys: Builders<MongoDBUISession>
                    .IndexKeys
                    .Ascending(session => session.SessionId),
                options: new CreateIndexOptions
                {
                    Unique = true,
                    Name = "uq_session_id"
                }
            ),
            cancellationToken: cancellationToken
        );
        await sessions.Indexes.CreateOneAsync(
            model: new CreateIndexModel<MongoDBUISession>(
                keys: Builders<MongoDBUISession>
                    .IndexKeys
                    .Ascending(session => session.ExpiresAt),
                options: new CreateIndexOptions
                {
                    ExpireAfter = TimeSpan.Zero,
                    Name = "ttl_expires_at"
                }
            ),
            cancellationToken: cancellationToken
        );
    }
}
