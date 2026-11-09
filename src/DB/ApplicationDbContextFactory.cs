using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ui.DB;

public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetValue<bool>("MIGRATOR_EXECUTE")
            ? PostgresConfiguration.GetConnectionString(configuration)
            : "Host=localhost;Database=dummy;Username=dummy";

        options.UseNpgsql(connectionString);
        options.UseOpenIddict();

        return new ApplicationDbContext(options.Options);
    }
}
