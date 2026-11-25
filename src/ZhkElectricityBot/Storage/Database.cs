using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ZhkElectricityBot.Storage;

/// <summary>Opens short-lived SQLite connections and creates the schema once.</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(IOptions<BotOptions> options)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS subscribers (
                chat_id     INTEGER PRIMARY KEY,
                username    TEXT,
                first_name  TEXT,
                subscribed  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS feature_settings (
                chat_id     INTEGER NOT NULL,
                feature     TEXT NOT NULL,
                enabled     INTEGER NOT NULL,
                PRIMARY KEY (chat_id, feature)
            );
            CREATE TABLE IF NOT EXISTS bot_state (
                key         TEXT PRIMARY KEY,
                value       TEXT NOT NULL,
                updated     TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }
}
