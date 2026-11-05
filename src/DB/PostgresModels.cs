using ui.Session;

namespace ui.DB;

public sealed record PostgresUIUserProfile(
    Guid SessionId
    , string Name
)
{
    public UIUserProfile ToDomain() => new(Name);
    public static PostgresUIUserProfile FromDomain(Guid sessionId, UIUserProfile profile) => new(sessionId, profile.Name);
}

public sealed record PostgresUISession(
    Guid SessionId
    , string AccessToken
    , DateTime ExpiresAt
)
{
    // EFCoreのためにpropertyにしている
    public PostgresUIUserProfile Profile { get; set; } = null!;

    public UISession ToDomain() =>
        new(
            SessionId: SessionId,
            AccessToken: AccessToken,
            AccessTokenExpiresAt: ExpiresAt,
            Profile: Profile.ToDomain()
        );

    public static PostgresUISession FromDomain(UISession session) =>
        new(
            SessionId: session.SessionId,
            AccessToken: session.AccessToken,
            ExpiresAt: session.AccessTokenExpiresAt.UtcDateTime
        )
        {
            Profile = PostgresUIUserProfile.FromDomain(session.SessionId, session.Profile)
        };
}
