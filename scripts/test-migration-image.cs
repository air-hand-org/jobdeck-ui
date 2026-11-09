#:property PublishAot=false
using System.Diagnostics;

// Run from the repository root against the already built production migrator image.
var image = args.Length == 1 ? args[0] : throw new ArgumentException("Expected migrator image reference");
var name = "migration-smoke-" + Guid.NewGuid().ToString("N");
try
{
    await Docker("network", "create", "--internal", name);
    await Docker("run", "--detach", "--name", name, "--network", name,
        "--env", "POSTGRES_HOST_AUTH_METHOD=trust", "--env", "POSTGRES_DB=smoke",
        "postgres:17");
    var ready = false;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        if ((await Run("exec", name, "pg_isready", "-h", "127.0.0.1", "-U", "postgres", "-d", "smoke")).Code == 0)
        {
            ready = true;
            break;
        }
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
    Check(ready, "PostgreSQL did not become ready");
    Check(await Sql("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'") == "0", "database must start empty");

    var imageId = await Docker("image", "inspect", "--format", "{{.Id}}", image);
    Console.WriteLine($"Testing production migrator {image}: {imageId}");
    Console.WriteLine("Applying image to empty database");
    await Migrate(imageId);
    var expected = new[] { "DataProtectionKeys", "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens", "Profiles", "Sessions", "__EFMigrationsHistory" };
    var tables = (await Sql("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name")).Split('\n');
    Check(tables.ToHashSet().SetEquals(expected), "expected application tables and EF history; got: " + string.Join(", ", tables));
    var history = await Sql("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
    var migrations = Directory.GetFiles("src/Migrations", "*.cs")
        .Where(path => !path.EndsWith(".Designer.cs") && !path.EndsWith("ModelSnapshot.cs"))
        .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);
    Check(history.Split('\n').SequenceEqual(migrations), "history must contain every checked-in migration");
    Console.WriteLine("Reapplying the same image ID");
    await Migrate(imageId);
    Check(await Sql("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"") == history, "repeat invocation must preserve migration history");
    Console.WriteLine("Checking that bundle connection errors propagate");
    var rejected = await Run("run", "--rm", "--network", name, imageId,
        "--connection", $"Host={name};Port=1;Database=smoke;Username=postgres;Timeout=1", "--verbose");
    Check(rejected.Code == 1, $"bundle connection failure must exit 1, got {rejected.Code}");
    Console.WriteLine("PASS migrator image empty database, schema/history, repeat invocation, and failure exit");
}
catch
{
    await Run("logs", name);
    throw;
}
finally
{
    await Run("rm", "--force", "--volumes", name);
    await Run("network", "rm", name);
}

async Task<string> Migrate(string imageId) => await Docker("run", "--rm", "--network", name, imageId,
    "--connection", $"Host={name};Database=smoke;Username=postgres;Timeout=5", "--verbose");
async Task<string> Sql(string sql) => await Docker("exec", name, "psql", "-U", "postgres", "-d", "smoke", "-v", "ON_ERROR_STOP=1", "-Atc", sql);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task<string> Docker(params string[] arguments)
{
    var result = await Run(arguments);
    Check(result.Code == 0, $"docker {arguments[0]} failed with exit {result.Code}");
    return result.Output.Trim();
}
static async Task<(int Code, string Output)> Run(params string[] arguments)
{
    var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start)!;
    var output = await process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync();
    Console.Write(output);
    return (process.ExitCode, output);
}
