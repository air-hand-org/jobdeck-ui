#:property PublishAot=false
using System.Diagnostics;

// Run from the repository root against the already built production migrator image.
var image = args.Length == 1 ? args[0] : throw new ArgumentException("Expected migrator image reference");
var name = "migration-smoke-" + Guid.NewGuid().ToString("N");
try
{
    await Docker("network", "create", name);
    await Docker("run", "--detach", "--name", name, "--network", name,
        "--env", "POSTGRES_HOST_AUTH_METHOD=trust", "--env", "POSTGRES_DB=smoke",
        "postgres:17");
    var ready = false;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        if ((await Run("exec", name, "pg_isready", "-U", "postgres", "-d", "smoke")).Code == 0)
        {
            ready = true;
            break;
        }
        await Task.Delay(TimeSpan.FromSeconds(1));
    }
    Check(ready, "PostgreSQL did not become ready");
    Check(await Sql("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public'") == "0", "database must start empty");

    // Schema assertions deliberately precede implementation for the RED run.
    var expected = new[] { "DataProtectionKeys", "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens", "Profiles", "Sessions", "__EFMigrationsHistory" };
    var tables = (await Sql("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name")).Split('\n');
    Check(tables.ToHashSet().SetEquals(expected), "expected application tables and EF history; got: " + string.Join(", ", tables));
    var history = await Sql("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
    var migrations = Directory.GetFiles("src/Migrations", "*.cs")
        .Where(path => !path.EndsWith(".Designer.cs") && !path.EndsWith("ModelSnapshot.cs"))
        .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);
    Check(history.Split('\n').SequenceEqual(migrations), "history must contain every checked-in migration");
    Console.WriteLine("PASS migrator image schema and migration history");
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
