namespace ZhkElectricityBot.Stations;

/// <summary>Persisted per station, so an outage in progress survives a restart or redeploy.</summary>
public sealed class StationState
{
    public bool? GridOn { get; set; }
    public DateTimeOffset? OutageStart { get; set; }
    public double? SocAtStart { get; set; }
    public DateTimeOffset? NextStatusAt { get; set; }
    public List<int> WarnedLevels { get; set; } = [];
    public DateTimeOffset? LastGridReturn { get; set; }
    public bool OfflineAlerted { get; set; }
}

/// <summary>How long the batteries will last.</summary>
public sealed record Forecast(TimeSpan Left, bool FromDischarge, double? SocDrop, TimeSpan? Over);
