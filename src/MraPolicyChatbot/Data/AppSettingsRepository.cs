using Dapper;
using MraPolicyChatbot.Models;

namespace MraPolicyChatbot.Data;

// Backs the Admin "Settings" page: a simple key/value table so an admin
// can change AI connection details, the answer-matching threshold, and the
// department list at runtime — no editing appsettings.json/C# code and
// rebuilding required. Values are always stored/read as plain text;
// callers are responsible for parsing (e.g. double.Parse for the
// threshold), the same way ASP.NET Core's own IConfiguration is just
// string key/value pairs underneath.
public class AppSettingsRepository
{
    public const string OllamaBaseUrlKey = "Ollama:BaseUrl";
    public const string OllamaEmbeddingModelKey = "Ollama:EmbeddingModel";
    public const string OllamaChatModelKey = "Ollama:ChatModel";
    public const string SimilarityThresholdKey = "Chat:SimilarityThreshold";
    public const string DepartmentListKey = "Departments:List";

    private readonly DbConnectionFactory _connectionFactory;

    public AppSettingsRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> GetValueAsync(string key)
    {
        using var connection = _connectionFactory.CreateConnection();
        const string sql = "SELECT [Value] FROM dbo.AppSettings WHERE [Key] = @Key;";
        return await connection.QuerySingleOrDefaultAsync<string?>(sql, new { Key = key });
    }

    public async Task SetValueAsync(string key, string? value)
    {
        using var connection = _connectionFactory.CreateConnection();
        // MERGE rather than a plain UPDATE so this also works the first
        // time a key is saved, in case the database was set up before this
        // key existed in database-setup.sql's seed section.
        const string sql = @"
            MERGE dbo.AppSettings AS target
            USING (SELECT @Key AS SettingKey) AS source
            ON target.[Key] = source.SettingKey
            WHEN MATCHED THEN UPDATE SET [Value] = @Value
            WHEN NOT MATCHED THEN INSERT ([Key], [Value]) VALUES (@Key, @Value);";
        await connection.ExecuteAsync(sql, new { Key = key, Value = value });
    }

    // Falls back to the original hardcoded list (Models/Departments.cs) if
    // the database has nothing yet — keeps Upload Policy/Edit Policy
    // working even against a database that predates this table.
    public async Task<List<string>> GetDepartmentListAsync()
    {
        var raw = await GetValueAsync(DepartmentListKey);
        var list = string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return list.Count > 0 ? list : Departments.All.ToList();
    }

    public Task SetDepartmentListAsync(List<string> departments) =>
        SetValueAsync(DepartmentListKey, string.Join('\n', departments));
}
