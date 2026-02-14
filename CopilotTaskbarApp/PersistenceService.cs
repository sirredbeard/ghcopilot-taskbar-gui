using Microsoft.Data.Sqlite;

namespace CopilotTaskbarApp;

public class PersistenceService
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public PersistenceService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CopilotTaskbarApp");
        
        Directory.CreateDirectory(appDataPath);
        
        _dbPath = Path.Combine(appDataPath, "chat.db");
        _connectionString = $"Data Source={_dbPath}";
        
        InitializeDatabase().GetAwaiter().GetResult();
    }

    private async Task InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var createTableCmd = connection.CreateCommand();
        createTableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                context TEXT
            )";
        
        await createTableCmd.ExecuteNonQueryAsync();
    }

    public async Task SaveMessageAsync(ChatMessage message)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var insertCmd = connection.CreateCommand();
        insertCmd.CommandText = @"
            INSERT INTO messages (role, content, timestamp, context)
            VALUES ($role, $content, $timestamp, $context)";
        
        insertCmd.Parameters.AddWithValue("$role", message.Role);
        insertCmd.Parameters.AddWithValue("$content", message.Content);
        insertCmd.Parameters.AddWithValue("$timestamp", message.Timestamp.ToString("o"));
        insertCmd.Parameters.AddWithValue("$context", message.Context ?? (object)DBNull.Value);
        
        await insertCmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ChatMessage>> LoadMessagesAsync(int limit = 100)
    {
        var messages = new List<ChatMessage>();

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = @"
            SELECT id, role, content, timestamp, context
            FROM messages
            ORDER BY timestamp DESC
            LIMIT $limit";
        
        selectCmd.Parameters.AddWithValue("$limit", limit);

        using var reader = await selectCmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            messages.Add(new ChatMessage
            {
                Id = reader.GetInt32(0),
                Role = reader.GetString(1),
                Content = reader.GetString(2),
                Timestamp = DateTime.Parse(reader.GetString(3)),
                Context = reader.IsDBNull(4) ? null : reader.GetString(4)
            });
        }

        messages.Reverse(); // Show oldest first
        return messages;
    }

    public async Task ClearHistoryAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var deleteCmd = connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM messages";
        
        await deleteCmd.ExecuteNonQueryAsync();
    }
}
