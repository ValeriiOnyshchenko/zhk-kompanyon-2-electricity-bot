using System.Text.Json;

namespace ZhkElectricityBot.Storage;

/// <summary>Key/value state that must survive redeploys (known schedules, sent reminders, outage progress).</summary>
public sealed class StateStore(Database database)
{
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM bot_state WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bot_state (key, value, updated) VALUES ($k, $v, $t)
            ON CONFLICT(key) DO UPDATE SET value = $v, updated = $t;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken ct) where T : class =>
        await GetAsync(key, ct) is { } json ? JsonSerializer.Deserialize<T>(json) : null;

    public Task SetJsonAsync<T>(string key, T value, CancellationToken ct) =>
        SetAsync(key, JsonSerializer.Serialize(value), ct);

    /// <summary>Removes keys with this prefix that were last updated before <paramref name="olderThan"/>.</summary>
    public async Task PruneAsync(string prefix, TimeSpan olderThan, CancellationToken ct)
    {
        await using var db = database.Open();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM bot_state WHERE key LIKE $p AND updated < $t;";
        cmd.Parameters.AddWithValue("$p", prefix + "%");
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.Subtract(olderThan).ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
