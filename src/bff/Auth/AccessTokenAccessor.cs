using System.Security.Claims;

namespace ui.Bff.Auth;

public sealed class AccessTokenAccessor(
    IHttpContextAccessor context
    , Session.IUISessionStore sessions
)
{
    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        var sid = context.HttpContext?.User.FindFirstValue("sid");
        if (!Guid.TryParse(sid, out var sessionId))
        {
            return null;
        }

        var session = await sessions.GetSessionAsync(sessionId, cancellationToken);
        return session?.AccessToken;
    }
}
