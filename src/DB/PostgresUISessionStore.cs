using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using ui.Session;

namespace ui.DB;

public sealed class PostgresUISessionStore(
    IDbContextFactory<ApplicationDbContext> dbFactory
    , TimeProvider timeProvider
    , IDataProtectionProvider provider
) : IUISessionStore
{
    private readonly IDataProtector protector = provider.CreateProtector("jobdeck-ui.Session.AccessToken.v1");

    public async Task<UISession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions
            .AsNoTracking()
            .Include(s => s.Profile)
            .SingleOrDefaultAsync(
                predicate: s => s.SessionId == sessionId && s.ExpiresAt > timeProvider.GetUtcNow().UtcDateTime
                , cancellationToken: cancellationToken
            );
        if (session is null)
        {
            return null;
        }
        return session.ToDomain() with
        {
            AccessToken = protector.Unprotect(session.AccessToken)
        }
        ;
    }

    public async Task SetSessionAsync(UISession session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var dbSession = PostgresUISession.FromDomain(session with
        {
            AccessToken = protector.Protect(session.AccessToken)
        });
        await db.Database.ExecuteSqlAsync(
            $"""
            WITH s AS (
                INSERT INTO "Sessions" (
                    "SessionId"
                    , "AccessToken"
                    , "ExpiresAt"
                ) VALUES (
                    {dbSession.SessionId}
                    , {dbSession.AccessToken}
                    , {dbSession.ExpiresAt}
                )
                ON CONFLICT ("SessionId")
                DO UPDATE SET
                    "AccessToken" = EXCLUDED."AccessToken"
                    , "ExpiresAt" = EXCLUDED."ExpiresAt"
                RETURNING *
            )
            INSERT INTO "Profiles" (
                "SessionId"
                , "Name"
            )
            SELECT
                s."SessionId"
                , {dbSession.Profile.Name}
            FROM s
            ON CONFLICT ("SessionId")
            DO UPDATE SET
                "Name" = EXCLUDED."Name"
            ;
            """
            , cancellationToken
        );
        await tx.CommitAsync(cancellationToken);
    }

    public async Task RemoveSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Sessions.Where(s => s.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken)
        ;
    }
}
