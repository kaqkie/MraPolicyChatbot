using System.Data;
using Microsoft.Data.SqlClient;

namespace MraPolicyChatbot.Data;

// Phase 2: creates raw ADO.NET connections for the repositories to use with
// Dapper. No connection pooling configuration, no retry logic, no ORM —
// just reads the connection string from configuration.
public class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' was not found in configuration.");

        // The SqlClient default connect timeout (15s) can be genuinely too
        // short right after the local SQL Server (Express) service has
        // just started — the first connection sometimes has to wait for it
        // to finish coming up, which shows up as "Connection Timeout
        // Expired... [Post-Login]" even though the server is fine and
        // responds normally moments later. Raised here rather than by
        // editing the connection string in appsettings.json, so it applies
        // no matter what's configured there. This does NOT fix a SQL
        // Server that's genuinely stopped or unreachable — that still
        // fails, just after waiting longer instead of failing fast; if
        // this keeps happening rather than being a one-off right after
        // starting the app, check that the SQL Server (SQLEXPRESS) service
        // — and, for a named instance like localhost\SQLEXPRESS, the SQL
        // Server Browser service too — is actually running (services.msc).
        var builder = new SqlConnectionStringBuilder(configuredConnectionString)
        {
            ConnectTimeout = 30,
        };
        _connectionString = builder.ConnectionString;
    }

    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}
