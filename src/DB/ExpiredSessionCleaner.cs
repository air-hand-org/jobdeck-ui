using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ui.DB;

public sealed partial class ExpiredSessionCleaner(
    IServiceScopeFactory scopeFactory
    , ILogger<ExpiredSessionCleaner> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var now = DateTime.UtcNow;

                    var deleted = await db.Sessions
                        .Where(s => s.ExpiresAt <= now)
                        .ExecuteDeleteAsync(cancellationToken);

                    LogDeletedCount(deleted);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete expired sessions");
                }

                await Task.Delay(
                    TimeSpan.FromMinutes(5), cancellationToken
                );
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation($"Stopping {nameof(ExpiredSessionCleaner)}");
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted {Count} expired sessions")]
    private partial void LogDeletedCount(int count);
}
