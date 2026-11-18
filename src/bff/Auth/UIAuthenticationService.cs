using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using ui.Client.Api;

namespace ui.Bff.Auth;

public sealed class UIAuthenticationService(Client.AuthApiClient authApi, Session.IUISessionStore sessions)
{
    public async Task<ApiResult> LoginAsync(Models.LoginFormModel model, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await authApi.AuthenticateLocalClientAsync(model, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ApiResult.Failure(result.Error ?? new ApiError(ApiErrorCode.Unauthenticated, "Login failed"));
        }

        return await SignInAsync(result.Value, model.Id!, httpContext, cancellationToken);
    }

    public async Task<ApiResult> GitHubLoginAsync(string externalToken, string name, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await authApi.ExchangeGitHubTokenAsync(externalToken, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ApiResult.Failure(result.Error ?? new ApiError(ApiErrorCode.Unauthenticated, "External login failed"));
        }

        return await SignInAsync(result.Value, name, httpContext, cancellationToken);
    }

    private async Task<ApiResult> SignInAsync(Jobdeck.Auth.V1.Token token, string name, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var session = new Session.UISession(
          Guid.NewGuid(),
          token.AccessToken,
          DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds),
          new Session.UIUserProfile(name)
        );
        await sessions.SetSessionAsync(session, cancellationToken);

        var principal = new ClaimsPrincipal(
          new ClaimsIdentity(
              [new Claim("sid", session.SessionId.ToString())],
              CookieAuthenticationDefaults.AuthenticationScheme
          )
        );
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = session.AccessTokenExpiresAt,
            IsPersistent = false,
            AllowRefresh = false,
        };
        await httpContext.SignInAsync(
          CookieAuthenticationDefaults.AuthenticationScheme,
          principal,
          properties
        );

        return ApiResult.Success();
    }

    public async Task LogoutAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var sid = httpContext.User.FindFirstValue("sid");
        if (Guid.TryParse(sid, out var sessionId))
        {
            await sessions.RemoveSessionAsync(sessionId, cancellationToken);
        }

        await httpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme
        );
    }
}
