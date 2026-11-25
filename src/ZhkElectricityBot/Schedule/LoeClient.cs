using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ZhkElectricityBot.Schedule;

/// <summary>Reads the outage schedule from the JSON API behind poweron.loe.lviv.ua.</summary>
public sealed class LoeClient(IHttpClientFactory http, IOptions<ScheduleOptions> options, ILogger<LoeClient> log)
{
    /// <summary>All published days (normally "Today" and, in the evening, "Tomorrow") for the configured group.</summary>
    public async Task<List<DaySchedule>> GetAsync(CancellationToken ct)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Get, o.Url);
        // The API sits behind a cache: always ask for the current version.
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.Accept.ParseAdd("application/ld+json, application/json");

        using var response = await http.CreateClient("web").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var result = new List<DaySchedule>();
        foreach (var item in MenuItems(json.RootElement))
        {
            var html = item.TryGetProperty("rawHtml", out var h) && h.ValueKind == JsonValueKind.String ? h.GetString() : null;
            if (string.IsNullOrWhiteSpace(html)) continue;

            var name = item.TryGetProperty("name", out var n) ? n.GetString() : "?";
            if (LoeParser.Parse(html, o.Group) is { } day)
            {
                if (day.Unrecognized) log.LogWarning("LOE {Item}: unrecognized line for group {Group}: {Line}", name, o.Group, day.Line);
                result.Add(day);
            }
            else
            {
                log.LogWarning("LOE {Item}: could not find the date or group {Group} in the schedule", name, o.Group);
            }
        }

        // The same date can appear twice (Today/Tomorrow overlap during re-publishing) — keep the newest version.
        return result.GroupBy(d => d.Date).Select(g => g.OrderByDescending(d => AsOfKey(d.AsOf)).First())
            .OrderBy(d => d.Date).ToList();
    }

    private static IEnumerable<JsonElement> MenuItems(JsonElement root)
    {
        if (!root.TryGetProperty("hydra:member", out var members) || members.ValueKind != JsonValueKind.Array) yield break;
        foreach (var menu in members.EnumerateArray())
        {
            if (!menu.TryGetProperty("menuItems", out var items) || items.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in items.EnumerateArray()) yield return item;
        }
    }

    /// <summary>"22:11 25.11.2026" → "2026-11-25 22:11" so it sorts.</summary>
    private static string AsOfKey(string asOf)
    {
        var p = asOf.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2) return asOf;
        var d = p[1].Split('.');
        return d.Length == 3 ? $"{d[2]}-{d[1]}-{d[0]} {p[0].PadLeft(5, '0')}" : asOf;
    }
}
