using Microsoft.Extensions.Options;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Stations;

namespace ZhkElectricityBot;

/// <summary>
/// One clock-aligned loop for all requests (LOE + Deye).
/// Normally every <see cref="PollingOptions.IdleIntervalMinutes"/> (5 → xx:00, xx:05, xx:10 …);
/// from an hour before a scheduled outage until an hour after it, and while a station runs on batteries
/// (plus an hour after power returns) — every <see cref="PollingOptions.ActiveIntervalMinutes"/> at the start of the minute.
/// </summary>
public sealed class PowerMonitorService(
    ScheduleService schedule,
    StationService stations,
    LocalClock clock,
    IOptions<PollingOptions> options,
    ILogger<PowerMonitorService> log) : BackgroundService
{
    private readonly PollingOptions _o = options.Value;
    private int? _lastInterval;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await schedule.LoadAsync(ct);
        await stations.LoadAsync(ct);

        // First check at the next full minute, then on the regular grid.
        var next = Slot(clock.UtcNow, 1);
        while (!ct.IsCancellationRequested)
        {
            var wait = next - clock.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock.Provider, ct);

            await TickAsync(ct);

            var interval = IsActive(clock.UtcNow) ? _o.ActiveIntervalMinutes : _o.IdleIntervalMinutes;
            if (interval != _lastInterval)
            {
                log.LogInformation("Polling every {Minutes} min", interval);
                _lastInterval = interval;
            }
            next = Slot(clock.UtcNow, interval);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        try { await schedule.TickAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Schedule check failed"); }

        try { await stations.TickAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Station check failed"); }
    }

    public bool IsActive(DateTimeOffset now)
    {
        var before = TimeSpan.FromMinutes(_o.ActiveBeforeOutageMinutes);
        var after = TimeSpan.FromMinutes(_o.ActiveAfterOutageMinutes);
        return schedule.Outages.Any(o => now >= o.Start - before && now <= o.End + after) || stations.IsActive(now, after);
    }

    /// <summary>Next clock boundary of the interval in local time (5 → :00/:05/:10…), strictly in the future.</summary>
    private DateTimeOffset Slot(DateTimeOffset nowUtc, int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 60);
        var local = clock.ToLocal(nowUtc);
        var start = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset);
        var slot = start.AddMinutes((local.Minute / minutes + 1) * minutes);
        return slot.ToUniversalTime();
    }
}
