using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using ui.Session;

namespace ui.Profile;

public sealed class UIUserProfileService(
    AuthenticationStateProvider provider
    , IUISessionStore sessions)
{
    public async Task<UIUserProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        var state = await provider.GetAuthenticationStateAsync();
        var sid = state.User.FindFirstValue("sid");
        if (!Guid.TryParse(sid, out var sessionId))
        {
            return null;
        }
        var session = await sessions.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }
        return session.Profile;
    }
}
