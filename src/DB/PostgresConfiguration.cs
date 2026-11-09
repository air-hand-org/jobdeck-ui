using Npgsql;

namespace ui.DB;

public sealed class PostgresConfiguration
{
    public static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrEmpty(connectionString))
        {
            return connectionString;
        }
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = Required(configuration, "POSTGRES_HOST"),
            Port = int.Parse(
                Required(configuration, "POSTGRES_PORT")
            ),
            Database = Required(configuration, "POSTGRES_DB"),
            Username = Required(configuration, "POSTGRES_USER"),
            Password = Required(configuration, "POSTGRES_PASSWORD"),
        };

        return connectionStringBuilder.ConnectionString;
    }

    private static string Required(IConfiguration configuration, string name) => configuration[name] ?? throw new InvalidOperationException($"{name} is required.");
}
