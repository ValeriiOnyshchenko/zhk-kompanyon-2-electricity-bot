namespace ZhkElectricityBot.Schedule;

/// <summary>One outage in minutes from local midnight; End may be 1440 ("до 24:00").</summary>
public sealed record Slot(int Start, int End)
{
    public override string ToString() => $"{Text.Minutes(Start)}–{Text.Minutes(End)}";
}

/// <summary>The schedule of one group for one day, as published by LOE.</summary>
public sealed class DaySchedule
{
    public DateOnly Date { get; set; }

    /// <summary>"Інформація станом на …" — LOE's version stamp, e.g. "22:11 25.11.2026".</summary>
    public string AsOf { get; set; } = "";

    /// <summary>The group's line as published, e.g. "Електроенергії немає з 13:30 до 17:00."</summary>
    public string Line { get; set; } = "";

    public List<Slot> Slots { get; set; } = [];

    /// <summary>The line said neither "є" nor any "з … до …" interval.</summary>
    public bool Unrecognized { get; set; }

    public string Signature => Unrecognized ? "?" + Line : string.Join(",", Slots);

    public TimeSpan Total => TimeSpan.FromMinutes(Slots.Sum(s => s.End - s.Start));
}

/// <summary>An outage in absolute time (touching slots of adjacent days are merged).</summary>
public sealed record Outage(DateTimeOffset Start, DateTimeOffset End)
{
    public TimeSpan Length => End - Start;
}
