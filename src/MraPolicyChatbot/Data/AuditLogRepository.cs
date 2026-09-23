using Dapper;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Data;

// Phase 2: basic data access for dbo.AuditLog using Dapper. All queries are
// parameterized. Only a Create method is needed for now — nothing in
// Phase 2 reads the audit log back yet.
public class AuditLogRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AuditLogRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(AuditLog entry)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.AuditLog (UserId, Action, EntityType, EntityId, Timestamp, Details)
            OUTPUT INSERTED.Id
            VALUES (@UserId, @Action, @EntityType, @EntityId, @Timestamp, @Details);";

        return await connection.QuerySingleAsync<int>(sql, entry);
    }
}
