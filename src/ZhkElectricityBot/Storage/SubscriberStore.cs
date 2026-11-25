namespace ZhkElectricityBot.Storage;

public sealed record Subscriber(long ChatId, string? Username, string? FirstName);

/// <summary>Users who pressed Start, and which features each of them switched off.</summary>
public sealed class SubscriberStore(Database database)
{
    public async Task AddAsync(long chatId, string? username, string? firstName, CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO subscribers (chat_id, username, first_name, subscribed)
            VALUES ($id, $u, $f, $s)
            ON CONFLICT(chat_id) DO UPDATE SET username = $u, first_name = $f;
            """;
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$u", (object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", (object?)firstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$s", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Keeps the stored username current (it decides who the owner is).</summary>
    public async Task TouchAsync(long chatId, string? username, string? firstName, CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE subscribers SET username = $u, first_name = $f WHERE chat_id = $id;";
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$u", (object?)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", (object?)firstName ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> RemoveAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM feature_settings WHERE chat_id = $id; DELETE FROM subscribers WHERE chat_id = $id;";
        cmd.Parameters.AddWithValue("$id", chatId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<Subscriber?> GetAsync(long chatId, CancellationToken ct = default) =>
        (await QueryAsync("SELECT chat_id, username, first_name FROM subscribers WHERE chat_id = $id;", chatId, ct)).FirstOrDefault();

    public Task<List<Subscriber>> GetAllAsync(CancellationToken ct = default) =>
        QueryAsync("SELECT chat_id, username, first_name FROM subscribers;", null, ct);

    /// <summary>Features this chat switched off (everything is on by default).</summary>
    public async Task<HashSet<string>> GetDisabledAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT feature FROM feature_settings WHERE chat_id = $id AND enabled = 0;";
        cmd.Parameters.AddWithValue("$id", chatId);
        var result = new HashSet<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>All (chat, feature) pairs that are switched off.</summary>
    public async Task<HashSet<(long ChatId, string Feature)>> GetAllDisabledAsync(CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT chat_id, feature FROM feature_settings WHERE enabled = 0;";
        var result = new HashSet<(long, string)>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) result.Add((r.GetInt64(0), r.GetString(1)));
        return result;
    }

    public async Task SetFeatureAsync(long chatId, string feature, bool enabled, CancellationToken ct = default)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO feature_settings (chat_id, feature, enabled) VALUES ($id, $f, $e)
            ON CONFLICT(chat_id, feature) DO UPDATE SET enabled = $e;
            """;
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$f", feature);
        cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<List<Subscriber>> QueryAsync(string sql, long? chatId, CancellationToken ct)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        if (chatId is { } id) cmd.Parameters.AddWithValue("$id", id);
        var result = new List<Subscriber>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(new Subscriber(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return result;
    }
}
