using Microsoft.Extensions.Options;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Stations;

namespace ZhkElectricityBot.Bot;

/// <summary>Builds the /status and /schedule answers for one user.</summary>
public sealed class StatusReport(
    StationService stations,
    ScheduleService schedule,
    LocalClock clock,
    IOptions<ScheduleOptions> scheduleOptions,
    ILogger<StatusReport> log)
{
    public string Schedule() => schedule.Enabled
        ? BotTexts.ScheduleOverview(schedule.Days, clock.Today, scheduleOptions.Value)
        : "Графік вимкнень вимкнено в налаштуваннях бота.";

    public async Task<string> StatusAsync(IReadOnlyList<Feature> available, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var parts = new List<string>();
        foreach (var s in stations.Stations.Where(s => available.Any(f => f.Id == s.Feature)))
        {
            if (s.Last is null && stations.Enabled)
            {
                try { await stations.ReadAsync(s, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Status read of {Key} failed: {Error}", s.Options.Key, ex.Message);
                }
            }
            var f = s.Last is { } r && r.GridOn == false ? stations.Forecast(s.State, r, now) : null;
            parts.Add(BotTexts.StationNow(s.Options.Title, s.Last, s.State, f, schedule.CurrentOutage(now), schedule.NextOutage(now), now, clock));
        }
        if (available.Any(f => f.Id == Features.Schedule)) parts.Add(Schedule());
        return parts.Count > 0 ? string.Join("\n\n— — —\n\n", parts) : BotTexts.NoData;
    }
}
