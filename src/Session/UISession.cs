namespace ui.Session;

public sealed record UIUserProfile(string Name);

public sealed record UISession(
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    UIUserProfile Profile
)
{
    public bool IsExpired(DateTimeOffset now) => AccessTokenExpiresAt <= now;
}
