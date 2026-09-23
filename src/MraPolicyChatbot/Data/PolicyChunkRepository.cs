using Dapper;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Data;

// Phase 2: basic CRUD data access for dbo.PolicyChunks using Dapper. All
// queries are parameterized. No chunking logic here (that's
// Services/TextChunker + DocumentProcessingService).
public class PolicyChunkRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public PolicyChunkRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<int> CreateAsync(PolicyChunk chunk)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            INSERT INTO dbo.PolicyChunks (PolicyId, ChunkText, ChunkOrder, Embedding, CreatedDate)
            OUTPUT INSERTED.Id
            VALUES (@PolicyId, @ChunkText, @ChunkOrder, @Embedding, @CreatedDate);";

        return await connection.QuerySingleAsync<int>(sql, chunk);
    }

    public async Task<IEnumerable<PolicyChunk>> GetByPolicyIdAsync(int policyId)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT Id, PolicyId, ChunkText, ChunkOrder, Embedding, CreatedDate
            FROM dbo.PolicyChunks
            WHERE PolicyId = @PolicyId
            ORDER BY ChunkOrder;";

        return await connection.QueryAsync<PolicyChunk>(sql, new { PolicyId = policyId });
    }

    // Phase 5: used by DocumentProcessingService to clear out chunks from a
    // previous run before inserting the new set (re-processing support).
    public async Task<int> DeleteByPolicyIdAsync(int policyId)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "DELETE FROM dbo.PolicyChunks WHERE PolicyId = @PolicyId;";

        return await connection.ExecuteAsync(sql, new { PolicyId = policyId });
    }

    // Phase 5: used by the admin policy list to show a chunk count per
    // policy without pulling full chunk text into memory.
    public async Task<int> GetCountByPolicyIdAsync(int policyId)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "SELECT COUNT(*) FROM dbo.PolicyChunks WHERE PolicyId = @PolicyId;";

        return await connection.QuerySingleAsync<int>(sql, new { PolicyId = policyId });
    }

    // Phase 6: retrieval source for chat Q&A — every chunk that has an
    // embedding, restricted to policies that are active and successfully
    // processed, paired with the parent policy's title for source citation.
    public async Task<IEnumerable<PolicyChunkWithPolicy>> GetAllWithEmbeddingsAsync()
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = @"
            SELECT c.Id, c.PolicyId, p.Title AS PolicyTitle, c.ChunkText, c.Embedding
            FROM dbo.PolicyChunks c
            INNER JOIN dbo.Policies p ON p.Id = c.PolicyId
            WHERE c.Embedding IS NOT NULL
              AND p.IsActive = 1
              AND p.IsProcessed = 1;";

        return await connection.QueryAsync<PolicyChunkWithPolicy>(sql);
    }
}
