using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ui.DB;

public sealed class ApplicationDbContextFactory
    : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        options.UseNpgsql(
            "Host=localhost;Database=dummy;Username=dummy"
        );
        options.UseOpenIddict();

        return new ApplicationDbContext(options.Options);
    }
}
