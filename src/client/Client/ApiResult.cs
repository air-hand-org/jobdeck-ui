
namespace ui.Client;

public enum ApiErrorCode
{
    Unauthenticated,
    PermissionDenied,
    Internal,
}

public sealed record ApiError(ApiErrorCode Code, string Message);

public sealed record ApiResult<T>(T? Value, ApiError? Error)
{
    public bool IsSuccess => Error is null;

    public static ApiResult<T> Success(T value) => new(Value: value, Error: null);
    public static ApiResult<T> Failure(ApiError error) => new(Value: default, Error: error);
}

public sealed record ApiResult(ApiError? Error)
{
    public bool IsSuccess => Error is null;
    public static ApiResult Success() => new(Error: null);
    public static ApiResult Failure(ApiError error) => new(error);
}
