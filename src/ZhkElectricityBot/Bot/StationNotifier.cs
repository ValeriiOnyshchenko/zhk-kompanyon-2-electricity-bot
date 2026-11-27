using Microsoft.Extensions.Options;
using ZhkElectricityBot.Stations;

namespace ZhkElectricityBot.Bot;

/// <summary>
/// Delivers station events. Power lost / hourly status / power back usually happen to all stations within a minute
/// or two, so each user gets them as ONE message with a section per station (in <see cref="StationOptions.Order"/>):
/// a user's message waits until every station they follow has the same event, or until
/// <see cref="BatteryOptions.CombineWindowMinutes"/> passed since the first one — then whatever is there goes out.
/// A user who follows a single station gets its events immediately. Other events are sent right away.
/// </summary>
public sealed class StationNotifier(
    Broadcaster broadcaster,
    IOptions<BatteryOptions> options,
    ILogger<StationNotifier> log)
{
    private readonly List<StationEvent> _pending = [];
    private readonly Lock _lock = new();

    public void Add(StationEvent e)
    {
        lock (_lock)
        {
            // The same thing again before it went out (e.g. a flapping grid) — keep the newest text only.
            var same = _pending.FirstOrDefault(p => p.Station == e.Station && p.Kind == e.Kind && p.SentTo.Count == 0);
            if (same is not null) same.Body = e.Body;
            else _pending.Add(e);
        }
    }

    public async Task FlushAsync(IReadOnlyList<StationService.Station> stations, DateTimeOffset now, CancellationToken ct)
    {
        List<StationEvent> events;
        lock (_lock) events = _pending.ToList();
        if (events.Count == 0) return;

        var window = TimeSpan.FromMinutes(Math.Max(0, options.Value.CombineWindowMinutes));
        var recipients = await broadcaster.RecipientsAsync(ct);

        foreach (var group in events.GroupBy(e => e.Kind))
        {
            var combinable = group.First().Combinable;
            foreach (var (chatId, features) in recipients)
            {
                var mine = stations.Where(s => features.Contains(s.Feature)).ToList();
                var due = group.Where(e => features.Contains(e.Station.Feature) && !e.SentTo.Contains(chatId))
                    .OrderBy(e => e.Station.Options.Order).ToList();
                if (due.Count == 0) continue;

                if (combinable)
                {
                    var complete = mine.All(s => group.Any(e => e.Station == s));
                    var expired = due.Any(e => now - e.At >= window);
                    if (!complete && !expired) continue; // wait for the other station(s)
                }
                else
                {
                    due = due.Take(1).ToList(); // one message per event
                }

                var text = string.Join("\n\n", due.Select(e => BotTexts.Section(e.Station.Options, e.Body)));
                await broadcaster.SendOneAsync(chatId, text, ct);
                foreach (var e in due) e.SentTo.Add(chatId);
                await Task.Delay(40, ct);
            }
        }

        // Forget events that every recipient got, and anything older than the window (all of those were sent above).
        lock (_lock)
        {
            _pending.RemoveAll(e =>
                now - e.At >= window && e.SentTo.Count > 0 ||
                recipients.Where(r => r.Features.Contains(e.Station.Feature)).All(r => e.SentTo.Contains(r.ChatId)));
        }
        log.LogDebug("Station notifier: {Count} pending", _pending.Count);
    }
}
