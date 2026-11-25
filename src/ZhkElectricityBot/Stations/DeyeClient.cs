using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ZhkElectricityBot.Stations;

public sealed record StationLatest(
    double? BatterySoc,
    double? ConsumptionPower,
    double? BatteryPower,   // > 0 discharging, < 0 charging
    double? WirePower,      // from the grid
    double? GenerationPower,
    DateTimeOffset? LastUpdate);

public sealed record DeviceInfo(string Sn, string Type);

/// <summary>Device data points: key → (value, unit), e.g. "GridVoltageL1" → ("229.10", "V").</summary>
public sealed class DeviceData(string sn, string type, Dictionary<string, (string Value, string Unit)> points)
{
    public string Sn { get; } = sn;
    public string Type { get; } = type;
    public IReadOnlyDictionary<string, (string Value, string Unit)> Points { get; } = points;

    public double? Number(string key) =>
        Points.TryGetValue(key, out var p) && double.TryParse(p.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;
}

/// <summary>Deye Cloud Open API (developer.deyecloud.com). Read-only calls only.</summary>
public sealed class DeyeClient(IHttpClientFactory http, IOptions<DeyeOptions> options, TimeProvider time, ILogger<DeyeClient> log)
{
    private readonly DeyeOptions _o = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public bool IsConfigured => _o.IsConfigured;

    public async Task<StationLatest> GetStationLatestAsync(long stationId, CancellationToken ct)
    {
        var r = await PostAsync("/station/latest", new { stationId }, ct);
        return new StationLatest(
            Num(r, "batterySOC"), Num(r, "consumptionPower"), Num(r, "batteryPower"),
            Num(r, "wirePower"), Num(r, "generationPower"),
            Num(r, "lastUpdateTime") is { } ts ? DateTimeOffset.FromUnixTimeSeconds((long)ts) : null);
    }

    public async Task<List<DeviceInfo>> GetStationDevicesAsync(long stationId, CancellationToken ct)
    {
        var r = await PostAsync("/station/device", new { page = 1, size = 50, stationIds = new[] { stationId } }, ct);
        var result = new List<DeviceInfo>();
        if (r.TryGetProperty("deviceListItems", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var d in items.EnumerateArray())
                result.Add(new DeviceInfo(Str(d, "deviceSn") ?? "", Str(d, "deviceType") ?? ""));
        return result.Where(d => d.Sn.Length > 0).ToList();
    }

    public async Task<List<DeviceData>> GetDeviceLatestAsync(IReadOnlyCollection<string> sns, CancellationToken ct)
    {
        var result = new List<DeviceData>();
        if (sns.Count == 0) return result;
        var r = await PostAsync("/device/latest", new { deviceList = sns }, ct);
        if (!r.TryGetProperty("deviceDataList", out var list) || list.ValueKind != JsonValueKind.Array) return result;
        foreach (var dev in list.EnumerateArray())
        {
            var points = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            if (dev.TryGetProperty("dataList", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var p in data.EnumerateArray())
                    if (Str(p, "key") is { } key)
                        points[key.Trim()] = (Str(p, "value") ?? "", Str(p, "unit") ?? "");
            result.Add(new DeviceData(Str(dev, "deviceSn") ?? "", Str(dev, "deviceType") ?? "", points));
        }
        return result;
    }

    // ---------------- transport ----------------

    private async Task<JsonElement> PostAsync(string path, object body, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(force: attempt > 1, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, _o.BaseUrl.TrimEnd('/') + path)
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Authorization = new("Bearer", token);

            using var response = await http.CreateClient("deye").SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 1) continue;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Deye {path}: HTTP {(int)response.StatusCode} {Short(text)}");

            var json = JsonDocument.Parse(text).RootElement.Clone();
            if (json.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                var msg = Str(json, "msg") ?? "";
                if (attempt == 1 && msg.Contains("token", StringComparison.OrdinalIgnoreCase)) continue; // expired token
                throw new InvalidOperationException($"Deye {path}: {Str(json, "code")} {msg}");
            }
            return json;
        }
    }

    private async Task<string> GetTokenAsync(bool force, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!force && _token is not null && time.GetUtcNow() < _tokenExpires) return _token;

            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_o.Password)));
            var url = $"{_o.BaseUrl.TrimEnd('/')}/account/token?appId={Uri.EscapeDataString(_o.AppId)}";
            using var response = await http.CreateClient("deye").PostAsJsonAsync(url,
                new { appSecret = _o.AppSecret, email = _o.Email, password = hash }, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            var json = JsonDocument.Parse(text).RootElement;

            var token = Str(json, "accessToken");
            if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(token))
                throw new InvalidOperationException($"Deye token: HTTP {(int)response.StatusCode} {Str(json, "code")} {Str(json, "msg")}");

            // expiresIn is in seconds; refresh a day early, and at least once a week.
            var seconds = Num(json, "expiresIn") ?? 0;
            var life = TimeSpan.FromSeconds(seconds > 0 ? seconds : 7 * 86400);
            if (life > TimeSpan.FromDays(7)) life = TimeSpan.FromDays(7);
            _tokenExpires = time.GetUtcNow() + (life > TimeSpan.FromDays(2) ? life - TimeSpan.FromDays(1) : life / 2);
            _token = token;
            log.LogInformation("Deye token obtained, valid until {Expires:u}", _tokenExpires);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            _ => null,
        } : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        } : null;

    private static string Short(string s) => s.Length > 200 ? s[..200] : s;
}
