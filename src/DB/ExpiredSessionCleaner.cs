using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace ui.DB;

[QuartzJob(Name = nameof(ExpiredSessionCleaner), Group = "maintenance", Description = "removes expired sessions")]
[CronTrigger("0 */5 * * * ?")]
[DisallowConcurrentExecution]
public sealed partial class ExpiredSessionCleaner(
    IServiceScopeFactory scopeFactory
    , TimeProvider timeProvider
    , ILogger<ExpiredSessionCleaner> logger
) : IJob
{
    public async ValueTask Execute(
        IJobExecutionContext context
        , CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var deleted = await db.Sessions
            .Where(s => s.ExpiresAt <= timeProvider.GetUtcNow().UtcDateTime)
            .ExecuteDeleteAsync(cancellationToken);

        LogDeletedCount(deleted);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted {Count} expired sessions")]
    private partial void LogDeletedCount(int count);
}
