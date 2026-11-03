using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Client.AspNetCore;
using OpenIddict.Client.WebIntegration;
using ui.Auth;

namespace ui.Endpoints;

public static class GitHubAuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapGitHubAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints
    )
    {
        var group = endpoints.MapGroup("/account/github");

        group.MapGet("/login", HandleLoginAsync);
        group.MapGet("/callback/login", HandleLoginCallbackAsync);

        return endpoints;
    }

    public static async Task<IResult> HandleLoginAsync()
    {
        return Results.Challenge(
            properties: new AuthenticationProperties {
                RedirectUri = "/"
            },
            authenticationSchemes: [
                OpenIddictClientWebIntegrationConstants.Providers.GitHub
            ]
        );
    }

    public static async Task<IResult> HandleLoginCallbackAsync(
        HttpContext context,
        UIAuthenticationService auth
    )
    {
        var result = await context.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Principal is null)
        {
            return Results.Unauthorized();
        }
        var userAccessToken = result.Properties?.GetTokenValue(OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken);
        if (string.IsNullOrEmpty(userAccessToken))
        {
            return Results.Unauthorized();
        }

        var name = result.Principal.FindFirstValue("name") ?? result.Principal.FindFirstValue("login");
        var loginResult = await auth.GitHubLoginAsync(userAccessToken, name!, context, context.RequestAborted);
        if (!loginResult.IsSuccess)
        {
            return Results.Unauthorized();
        }

        return Results.LocalRedirect(result.Properties?.RedirectUri ?? "/");
    }
}
