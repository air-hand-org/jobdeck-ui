using System.Diagnostics;
using Grpc.Core;
using Jobdeck.Auth.V1;
using ui.Models;
using ui.Telemetry;

namespace ui.Client;

public sealed class AuthApiClient(AuthService.AuthServiceClient auth, ILogger<AuthApiClient> logger)
{
    public async Task<ApiResult<Token>> AuthenticateLocalClientAsync(LoginFormModel model, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("auth.login", ActivityKind.Internal);

        try
        {
            var response = await auth.AuthenticateLocalClientAsync(
                new AuthenticateLocalClientRequest
                {
                    ClientId = model.Id,
                    ClientSecret = model.Password,
                    Audience = "jobdeck-job-api",
                },
                cancellationToken: cancellationToken
            );

            return ApiResult<Token>.Success(response.Token);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Auth API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<Token>.Failure(ApiError.FromRPCException(ex));
        }
    }

    public async Task<ApiResult<Token>> ExchangeGitHubTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await ExchangeExternalTokenAsync(token, ExternalTokenProvider.Github, cancellationToken);
    }

    private async Task<ApiResult<Token>> ExchangeExternalTokenAsync(string token, ExternalTokenProvider provider, CancellationToken cancellationToken = default)
    {
        using var activity = UiTelemetry.Source.StartActivity("auth.external", ActivityKind.Internal);

        try
        {
            var response = await auth.ExchangeExternalTokenAsync(
                new ExchangeExternalTokenRequest
                {
                    Provider = provider,
                    Token = token,
                    Audience = "jobdeck-job-api",
                },
                cancellationToken: cancellationToken
            );

            return ApiResult<Token>.Success(response.Token);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Auth API request failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Status.Detail);
            activity?.AddException(ex);
            return ApiResult<Token>.Failure(ApiError.FromRPCException(ex));
        }
    }
}
