using Dapper;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Data;

// Phase 2: basic CRUD data access for dbo.Users using Dapper. All queries
// are parameterized. No password hashing, validation, or business rules —
// that belongs to a later phase.
public class UserRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public UserRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // General-purpose "list every user" query. Not currently called by any
    // controller (the temporary Phase 2 debug endpoint that used to call this
    // — an unauthenticated API route that returned every user's password
    // hash — has been removed), but kept as a data-access method for future
    // admin/user-management screens.
    public async Task<IEnumerable<User>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, Username, PasswordHash, Role, CreatedDate, IsActive
            FROM dbo.Users
            ORDER BY Username;";

        return await connection.QueryAsync<User>(sql);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, Username, PasswordHash, Role, CreatedDate, IsActive
            FROM dbo.Users
            WHERE Username = @Username;";

        return await connection.QuerySingleOrDefaultAsync<User>(sql, new { Username = username });
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, Username, PasswordHash, Role, CreatedDate, IsActive
            FROM dbo.Users
            WHERE Id = @Id;";

        return await connection.QuerySingleOrDefaultAsync<User>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(User user)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Users (Username, PasswordHash, Role, CreatedDate, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @PasswordHash, @Role, @CreatedDate, @IsActive);";

        return await connection.QuerySingleAsync<int>(sql, user);
    }

    public async Task<int> UpdateAsync(User user)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            UPDATE dbo.Users
            SET Username = @Username,
                PasswordHash = @PasswordHash,
                Role = @Role,
                IsActive = @IsActive
            WHERE Id = @Id;";

        return await connection.ExecuteAsync(sql, user);
    }
}
