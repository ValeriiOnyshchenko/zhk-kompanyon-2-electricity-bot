using Microsoft.Extensions.Options;

namespace ZhkElectricityBot;

/// <summary>Current time and conversions to the configured local zone (Europe/Kyiv).</summary>
public sealed class LocalClock(TimeProvider time, IOptions<BotOptions> options)
{
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateTimeOffset Now => ToLocal(UtcNow);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Zone);

    /// <summary>Local wall-clock time on <paramref name="date"/> (<paramref name="minutes"/> may be 1440 = next midnight).</summary>
    public DateTimeOffset At(DateOnly date, int minutes)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    public TimeProvider Provider => time;
}
