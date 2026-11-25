using Microsoft.Extensions.Options;
using ZhkElectricityBot.Bot;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Storage;

namespace ZhkElectricityBot.Stations;

/// <summary>
/// Features 2 and 3 — one per Deye station (same logic, different audience).
/// Every check reads the station; notifies when it switches to batteries and back, sends battery status every
/// <see cref="BatteryOptions.StatusIntervalMinutes"/> during an outage, and warns at the configured charge levels.
/// </summary>
public sealed class StationService(
    DeyeClient deye,
    ScheduleService schedule,
    StateStore store,
    Broadcaster broadcaster,
    LocalClock clock,
    IOptions<List<StationOptions>> stations,
    IOptions<BatteryOptions> battery,
    ILogger<StationService> log)
{
    private readonly BatteryOptions _b = battery.Value;
    private readonly List<Station> _stations = stations.Value.Where(s => s.StationId > 0).Select(s => new Station(s)).ToList();

    public sealed class Station(StationOptions options)
    {
        public StationOptions Options { get; } = options;
        public string Feature => Features.Station(Options.Key);
        public string StateKey => $"station:{Options.Key}";
        public StationState State { get; set; } = new();
        public StationReading? Last { get; set; }
        public DateTimeOffset? LastReadAt { get; set; }
        public List<string> InverterSns { get; set; } = [];
        public DateTimeOffset DevicesReadAt { get; set; }
    }

    public IReadOnlyList<Station> Stations => _stations;

    public bool Enabled => deye.IsConfigured && _stations.Count > 0;

    /// <summary>Some station runs on batteries now, or city power came back less than <paramref name="after"/> ago.</summary>
    public bool IsActive(DateTimeOffset now, TimeSpan after) =>
        _stations.Any(s => s.State.GridOn == false || s.State.LastGridReturn is { } r && now - r <= after);

    public async Task LoadAsync(CancellationToken ct)
    {
        if (!deye.IsConfigured)
        {
            log.LogWarning("Deye credentials are not set — station features are off");
            return;
        }
        foreach (var s in _stations)
            s.State = await store.GetJsonAsync<StationState>(s.StateKey, ct) ?? new StationState();
        log.LogInformation("Stations: {List}", string.Join(", ", _stations.Select(s =>
            $"{s.Options.Key} (grid {(s.State.GridOn is { } g ? g ? "on" : "off" : "unknown")})")));
    }

    public async Task TickAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        foreach (var s in _stations)
        {
            try
            {
                await TickStationAsync(s, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Station {Key}: check failed: {Error}", s.Options.Key, ex.Message);
            }
        }
    }

    /// <summary>Reads the station now (used by the checks and, if nothing was read yet, by /status).</summary>
    public async Task<StationReading> ReadAsync(Station s, CancellationToken ct)
    {
        if (s.InverterSns.Count == 0 || clock.UtcNow - s.DevicesReadAt > TimeSpan.FromHours(6))
        {
            var devices = await deye.GetStationDevicesAsync(s.Options.StationId, ct);
            s.InverterSns = devices.Where(d => d.Type.Equals("INVERTER", StringComparison.OrdinalIgnoreCase)).Select(d => d.Sn).ToList();
            s.DevicesReadAt = clock.UtcNow;
            log.LogInformation("Station {Key}: {Count} inverter(s)", s.Options.Key, s.InverterSns.Count);
        }

        var latest = await deye.GetStationLatestAsync(s.Options.StationId, ct);
        var inverters = await deye.GetDeviceLatestAsync(s.InverterSns, ct);
        var reading = StationReading.From(latest, inverters, _b, s.Options.CapacityKwh);

        // Capacity barely changes; keep the last known value if one read lacks it.
        if (reading.CapacityKwh is null && s.Last?.CapacityKwh is { } cap)
            reading = new StationReading
            {
                Soc = reading.Soc, LoadW = reading.LoadW, BatteryW = reading.BatteryW, GridW = reading.GridW,
                GridVoltages = reading.GridVoltages, GridOn = reading.GridOn, UpdatedUtc = reading.UpdatedUtc,
                CapacityKwh = cap,
            };

        s.Last = reading;
        s.LastReadAt = clock.UtcNow;
        return reading;
    }

    private async Task TickStationAsync(Station s, CancellationToken ct)
    {
        var r = await ReadAsync(s, ct);
        var now = clock.UtcNow;
        var st = s.State;
        var changed = false;
        var title = s.Options.Title;

        // ---- stale data: the logger (or its internet) is down ----
        var age = r.UpdatedUtc is { } u ? now - u : TimeSpan.Zero;
        if (age > TimeSpan.FromMinutes(_b.StaleDataMinutes))
        {
            if (!st.OfflineAlerted)
            {
                st.OfflineAlerted = true;
                await SaveAsync(s, ct);
                await broadcaster.SendAsync(s.Feature, BotTexts.StationOffline(title, age, r.UpdatedUtc, clock), ct);
            }
            return; // old data says nothing about the grid now
        }
        if (st.OfflineAlerted)
        {
            st.OfflineAlerted = false;
            changed = true;
            await broadcaster.SendAsync(s.Feature, BotTexts.StationOnline(title), ct);
        }

        log.LogDebug("Station {Key}: grid {Grid}, SOC {Soc}, load {Load} W", s.Options.Key, r.GridOn, r.Soc, r.LoadW);

        if (r.GridOn is not { } gridOn)
        {
            if (changed) await SaveAsync(s, ct);
            return;
        }

        if (st.GridOn is null)
        {
            // First reading ever: remember the current state silently.
            st.GridOn = gridOn;
            if (!gridOn) StartOutage(st, r, now);
            log.LogInformation("Station {Key}: initial state — grid {Grid}", s.Options.Key, gridOn ? "on" : "off");
            await SaveAsync(s, ct);
            return;
        }

        if (st.GridOn == true && !gridOn)
        {
            st.GridOn = false;
            StartOutage(st, r, now);
            await SaveAsync(s, ct);
            log.LogInformation("Station {Key}: grid lost", s.Options.Key);
            await broadcaster.SendAsync(s.Feature, BotTexts.GridLost(title, r, Forecast(st, r, now), schedule.CurrentOutage(now), now, clock), ct);
            return;
        }

        if (st.GridOn == false && gridOn)
        {
            var duration = st.OutageStart is { } start ? now - start : (TimeSpan?)null;
            var used = st.SocAtStart is { } s0 && r.Soc is { } s1 ? s0 - s1 : (double?)null;
            st.GridOn = true;
            st.OutageStart = null;
            st.SocAtStart = null;
            st.NextStatusAt = null;
            st.WarnedLevels = [];
            st.LastGridReturn = now;
            await SaveAsync(s, ct);
            log.LogInformation("Station {Key}: grid back after {Duration}", s.Options.Key, duration);
            await broadcaster.SendAsync(s.Feature, BotTexts.GridBack(title, r, duration, used), ct);
            return;
        }

        if (!gridOn)
        {
            // ---- outage in progress: warnings, then the regular status ----
            var interval = TimeSpan.FromMinutes(Math.Max(1, _b.StatusIntervalMinutes));
            if (r.Soc is { } soc)
            {
                var crossed = _b.WarnSocLevels.Where(l => soc <= l && !st.WarnedLevels.Contains(l)).OrderBy(l => l).ToList();
                if (crossed.Count > 0)
                {
                    // Dropping past several levels at once (e.g. after a gap in data) → one warning, for the lowest.
                    st.WarnedLevels.AddRange(_b.WarnSocLevels.Where(l => l >= crossed[0] && !st.WarnedLevels.Contains(l)));
                    // The warning already carries the status — don't send both at once.
                    while (st.NextStatusAt <= now) st.NextStatusAt += interval;
                    await SaveAsync(s, ct);
                    await broadcaster.SendAsync(s.Feature, BotTexts.LowBattery(title, crossed[0], r, Forecast(st, r, now), now, clock), ct);
                }
            }

            if (st.NextStatusAt is { } next && now >= next)
            {
                while (st.NextStatusAt <= now) st.NextStatusAt += interval;
                await SaveAsync(s, ct);
                changed = false;
                await broadcaster.SendAsync(s.Feature,
                    BotTexts.BatteryStatus(title, r, st, Forecast(st, r, now), schedule.CurrentOutage(now), now, clock), ct);
            }
        }

        if (changed) await SaveAsync(s, ct);
    }

    private void StartOutage(StationState st, StationReading r, DateTimeOffset now)
    {
        st.OutageStart = now;
        st.SocAtStart = r.Soc;
        st.WarnedLevels = [];
        st.NextStatusAt = now + TimeSpan.FromMinutes(Math.Max(1, _b.StatusIntervalMinutes));
    }

    /// <summary>
    /// Time left until <see cref="BatteryOptions.ReserveSocPercent"/>.
    /// Preferably from the real discharge since the outage began (a short peak of load doesn't skew it);
    /// early in the outage, from the current load and the battery capacity.
    /// </summary>
    public Forecast? Forecast(StationState st, StationReading r, DateTimeOffset now)
    {
        if (r.Soc is not { } soc) return null;
        var usable = soc - _b.ReserveSocPercent;
        if (usable <= 0) return new Forecast(TimeSpan.Zero, false, null, null);

        if (st.OutageStart is { } start && st.SocAtStart is { } s0)
        {
            var elapsed = now - start;
            var drop = s0 - soc;
            if (elapsed >= TimeSpan.FromMinutes(_b.MinForecastMinutes) && drop >= _b.MinForecastSocDrop)
                return new Forecast(TimeSpan.FromHours(usable / (drop / elapsed.TotalHours)), true, drop, elapsed);
        }

        var drawW = r.BatteryW is > 0 ? r.BatteryW.Value : r.LoadW ?? 0;
        if (drawW > 10 && r.CapacityKwh is { } cap and > 0)
            return new Forecast(TimeSpan.FromHours(usable / 100 * cap / (drawW / 1000)), false, null, null);

        return null;
    }

    private Task SaveAsync(Station s, CancellationToken ct) => store.SetJsonAsync(s.StateKey, s.State, ct);
}
