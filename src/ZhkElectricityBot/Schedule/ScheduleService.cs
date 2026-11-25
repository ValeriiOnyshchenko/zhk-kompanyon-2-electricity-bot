using Microsoft.Extensions.Options;
using ZhkElectricityBot.Bot;
using ZhkElectricityBot.Storage;

namespace ZhkElectricityBot.Schedule;

/// <summary>
/// Feature 1 — the LOE outage schedule of one group.
/// Every check: a newly published day with outages → message; any change of the group's intervals → message;
/// <see cref="ScheduleOptions.ReminderMinutes"/> before every outage → reminder.
/// Known days and sent reminders are stored, so restarts and redeploys never repeat a message.
/// </summary>
public sealed class ScheduleService(
    LoeClient loe,
    StateStore state,
    Broadcaster broadcaster,
    LocalClock clock,
    IOptions<ScheduleOptions> options,
    ILogger<ScheduleService> log)
{
    private const string DayKey = "loe:day:";
    private const string ReminderKey = "loe:reminded:";
    private readonly ScheduleOptions _o = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<DaySchedule> _days = [];
    private DateOnly _pruned;

    public bool Enabled => _o.Enabled;

    /// <summary>Today and later, as last read from LOE (or from the database right after a restart).</summary>
    public IReadOnlyList<DaySchedule> Days => _days.Where(d => d.Date >= clock.Today).ToList();

    /// <summary>All known outages in absolute time, adjacent ones merged (20:30–24:00 + 00:00–02:00 = one).</summary>
    public IReadOnlyList<Outage> Outages
    {
        get
        {
            var result = new List<Outage>();
            foreach (var (day, slot) in _days.OrderBy(d => d.Date).SelectMany(d => d.Slots.Select(s => (d.Date, s))))
            {
                var o = new Outage(clock.At(day, slot.Start), clock.At(day, slot.End));
                if (result.Count > 0 && result[^1].End >= o.Start)
                    result[^1] = result[^1] with { End = o.End > result[^1].End ? o.End : result[^1].End };
                else
                    result.Add(o);
            }
            return result;
        }
    }

    /// <summary>
    /// The scheduled outage that explains a real one happening now: it starts within the next hour (power can go
    /// early) or is under way, or ended less than an hour ago (switching back on can take up to an hour).
    /// </summary>
    public Outage? CurrentOutage(DateTimeOffset now) =>
        Outages.Where(o => now >= o.Start - TimeSpan.FromMinutes(60) && now <= o.End + TimeSpan.FromMinutes(60))
            .OrderBy(o => Math.Abs((o.Start - now).TotalMinutes))
            .FirstOrDefault();

    public Outage? NextOutage(DateTimeOffset now) => Outages.FirstOrDefault(o => o.Start > now);

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!_o.Enabled) return;
        var today = clock.Today;
        var days = new List<DaySchedule>();
        for (var d = today.AddDays(-1); d <= today.AddDays(1); d = d.AddDays(1))
            if (await state.GetJsonAsync<DaySchedule>(DayKey + d.ToString("yyyy-MM-dd"), ct) is { } known) days.Add(known);
        _days = days;
        log.LogInformation("Schedule: {Count} known day(s) loaded for group {Group}", days.Count, _o.Group);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        if (!_o.Enabled) return;
        await _lock.WaitAsync(ct);
        try
        {
            await RefreshAsync(ct);
            await RemindAsync(ct);
            await PruneAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        List<DaySchedule> published;
        try
        {
            published = await loe.GetAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("LOE schedule could not be read: {Error}", ex.Message);
            return;
        }

        var today = clock.Today;
        foreach (var day in published.Where(d => d.Date >= today))
        {
            var key = DayKey + day.Date.ToString("yyyy-MM-dd");
            var known = await state.GetJsonAsync<DaySchedule>(key, ct);

            if (known is null)
            {
                log.LogInformation("Schedule for {Date} published: {Signature} (as of {AsOf})", day.Date, day.Signature, day.AsOf);
                if (day.Slots.Count > 0 || day.Unrecognized)
                    await broadcaster.SendAsync(Features.Schedule, BotTexts.SchedulePublished(day, _o), ct);
            }
            else if (known.Signature != day.Signature)
            {
                log.LogInformation("Schedule for {Date} changed: {Old} → {New} (as of {AsOf})", day.Date, known.Signature, day.Signature, day.AsOf);
                await broadcaster.SendAsync(Features.Schedule, BotTexts.ScheduleChanged(known, day, _o), ct);
            }

            if (known is null || known.Signature != day.Signature || known.AsOf != day.AsOf)
                await state.SetJsonAsync(key, day, ct);

            _days.RemoveAll(d => d.Date == day.Date);
            _days.Add(day);
        }
        _days.RemoveAll(d => d.Date < today.AddDays(-1));
    }

    private async Task RemindAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var lead = TimeSpan.FromMinutes(_o.ReminderMinutes);
        foreach (var outage in Outages.Where(o => o.Start > now && o.Start - now <= lead))
        {
            var key = ReminderKey + outage.Start.UtcDateTime.ToString("yyyy-MM-ddTHH:mm");
            if (await state.GetAsync(key, ct) is not null) continue;
            await state.SetAsync(key, "sent", ct); // mark first: a crash mid-broadcast must not resend it
            await broadcaster.SendAsync(Features.Schedule, BotTexts.OutageReminder(outage, clock.ToLocal(outage.Start) - clock.ToLocal(now), clock, _o), ct);
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        if (_pruned == clock.Today) return;
        _pruned = clock.Today;
        await state.PruneAsync(DayKey, TimeSpan.FromDays(7), ct);
        await state.PruneAsync(ReminderKey, TimeSpan.FromDays(7), ct);
    }
}
