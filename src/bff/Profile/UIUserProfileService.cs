using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ui.Bff.Profile;

public sealed class UIUserProfileService(
    AuthenticationStateProvider provider
    , Session.IUISessionStore sessions)
{
    public async Task<Session.UIUserProfile?> GetProfileAsync(CancellationToken cancellationToken = default)
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
