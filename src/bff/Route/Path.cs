namespace ui.Bff.Route;

public sealed class Path
{
    public static bool IsGrpcRequest(PathString path)
    {
        return path.Value?.StartsWith("/jobdeck.", StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
