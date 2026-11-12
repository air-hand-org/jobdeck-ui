using Grpc.Core;

namespace ui.Client;

public static class BackendErrors
{
    public static ApiError FromRPCException(RpcException ex)
    {
        return ex.StatusCode switch
        {
            StatusCode.Unauthenticated =>
                new(ApiErrorCode.Unauthenticated, "ログインの有効期限が切れました"),
            StatusCode.PermissionDenied =>
                new(ApiErrorCode.PermissionDenied, "この操作を実行する権限が足りません"),
            _ =>
                new(ApiErrorCode.Internal, "処理に失敗しました。再読み込みして結果を確認してください。"),
        };
    }
}
