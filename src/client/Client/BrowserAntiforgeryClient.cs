using Grpc.Core;
using Jobdeck.Bff.V1;

namespace ui.Client;

public sealed class BrowserAntiforgeryClient(AntiforgeryService.AntiforgeryServiceClient antiforgery)
{
    public async Task<ApiResult<GetAntiforgeryTokenResponse>> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await antiforgery.GetAntiforgeryTokenAsync(
                new GetAntiforgeryTokenRequest(),
                cancellationToken: cancellationToken
            );
            return ApiResult<GetAntiforgeryTokenResponse>.Success(result);
        }
        catch (RpcException ex)
        {
            return ex.StatusCode switch
            {
                StatusCode.Unauthenticated => ApiResult<GetAntiforgeryTokenResponse>.Failure(new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました")),
                StatusCode.PermissionDenied => ApiResult<GetAntiforgeryTokenResponse>.Failure(new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません。再読み込みしてください。")),
                _ => Failure(),
            };
        }
    }

    private static ApiResult<GetAntiforgeryTokenResponse> Failure() => ApiResult<GetAntiforgeryTokenResponse>.Failure(new(ApiErrorCode.Internal, "通信に失敗しました。接続を確認して再読み込みしてください。"));
}
