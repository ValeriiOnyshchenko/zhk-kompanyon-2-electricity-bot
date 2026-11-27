namespace ZhkElectricityBot.Stations;

public enum StationEventKind { GridLost, Status, GridBack, LowBattery, Offline, Online }

/// <summary>Something to tell about one station. <see cref="Body"/> is the text under the station's header line.</summary>
public sealed class StationEvent(StationService.Station station, StationEventKind kind, string body, DateTimeOffset at)
{
    public StationService.Station Station { get; } = station;
    public StationEventKind Kind { get; } = kind;
    public string Body { get; set; } = body;
    public DateTimeOffset At { get; } = at;

    /// <summary>Chats that already received this event.</summary>
    public HashSet<long> SentTo { get; } = [];

    /// <summary>These happen to all stations at about the same time, so they are merged into one message.</summary>
    public bool Combinable => Kind is StationEventKind.GridLost or StationEventKind.Status or StationEventKind.GridBack;
}
