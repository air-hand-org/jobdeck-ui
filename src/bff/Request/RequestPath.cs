namespace ui.Bff.Request;

public static class RequestPath
{
    public static bool IsGrpcRequest(PathString path)
    {
        return path.Value?.StartsWith("/jobdeck.", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
