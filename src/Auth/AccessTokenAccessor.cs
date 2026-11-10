using System.Security.Claims;

using ui.Session;

namespace ui.Auth;

public sealed class AccessTokenAccessor(
    IHttpContextAccessor context
    , IUISessionStore sessions
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
