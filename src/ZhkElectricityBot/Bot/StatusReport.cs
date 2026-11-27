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
            parts.Add(BotTexts.Section(s.Options, s.FailingSince is not null || s.Last is null
                ? BotTexts.StationUnreachable(s.Last?.UpdatedUtc, clock)
                : BotTexts.StationNow(s.Last, s.State, f, schedule.CurrentOutage(now), now, clock)));
        }
        return parts.Count > 0 ? string.Join("\n\n", parts) : BotTexts.NoData;
    }
}
