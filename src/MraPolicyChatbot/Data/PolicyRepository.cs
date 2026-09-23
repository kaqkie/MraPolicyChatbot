using Dapper;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Data;

// Phase 2: basic CRUD data access for dbo.Policies using Dapper. All
// queries are parameterized. No file upload handling, no validation, no
// business rules — that belongs to a later phase.
public class PolicyRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public PolicyRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IEnumerable<Policy>> GetAllAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, Title, Category, FilePath, ContentText, UploadedBy, UploadDate, Version, IsActive, IsProcessed, ProcessingError
            FROM dbo.Policies
            ORDER BY Title;";

        return await connection.QueryAsync<Policy>(sql);
    }

    public async Task<Policy?> GetByIdAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, Title, Category, FilePath, ContentText, UploadedBy, UploadDate, Version, IsActive, IsProcessed, ProcessingError
            FROM dbo.Policies
            WHERE Id = @Id;";

        return await connection.QuerySingleOrDefaultAsync<Policy>(sql, new { Id = id });
    }

    public async Task<int> CreateAsync(Policy policy)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.Policies (Title, Category, FilePath, ContentText, UploadedBy, UploadDate, Version, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Title, @Category, @FilePath, @ContentText, @UploadedBy, @UploadDate, @Version, @IsActive);";

        return await connection.QuerySingleAsync<int>(sql, policy);
    }

    // Used by the admin Edit Policy screen. Updates Title/Category always,
    // and also FilePath/Version/IsProcessed/ProcessingError when the admin
    // replaced the underlying file (AdminController.EditPolicy sets those
    // on the Policy object before calling this; otherwise they're just
    // written back unchanged).
    public async Task<int> UpdateAsync(Policy policy)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            UPDATE dbo.Policies
            SET Title = @Title,
                Category = @Category,
                FilePath = @FilePath,
                Version = @Version,
                IsProcessed = @IsProcessed,
                ProcessingError = @ProcessingError
            WHERE Id = @Id;";

        return await connection.ExecuteAsync(sql, policy);
    }

    // Phase 5: called by DocumentProcessingService after an extract/chunk
    // run — records whether it succeeded, the full extracted text (for the
    // previously-unused ContentText column), and any error message.
    public async Task<int> UpdateProcessingResultAsync(int id, bool isProcessed, string? contentText, string? processingError)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            UPDATE dbo.Policies
            SET IsProcessed = @IsProcessed,
                ContentText = @ContentText,
                ProcessingError = @ProcessingError
            WHERE Id = @Id;";

        return await connection.ExecuteAsync(sql, new { Id = id, IsProcessed = isProcessed, ContentText = contentText, ProcessingError = processingError });
    }

    public async Task<int> DeleteAsync(int id)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "DELETE FROM dbo.Policies WHERE Id = @Id;";

        return await connection.ExecuteAsync(sql, new { Id = id });
    }
}
