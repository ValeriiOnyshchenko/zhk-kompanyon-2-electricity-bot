using Microsoft.Extensions.Options;
using ZhkElectricityBot.Storage;

namespace ZhkElectricityBot.Bot;

public sealed record Feature(string Id, string Title, bool OwnerOnly);

/// <summary>
/// The bot's features: the LOE outage schedule and one feature per Deye station.
/// Owner-only stations are visible to the owner (Bot:OwnerUsername) alone.
/// </summary>
public sealed class Features
{
    public const string Schedule = "schedule";
    public static string Station(string key) => $"station:{key}";

    private readonly string _owner;

    public Features(IOptions<BotOptions> bot, IOptions<ScheduleOptions> schedule, IOptions<List<StationOptions>> stations)
    {
        _owner = Normalize(bot.Value.OwnerUsername);
        var list = new List<Feature>();
        if (schedule.Value.Enabled)
            list.Add(new Feature(Schedule, $"Графік вимкнень (група {schedule.Value.Group})", false));
        foreach (var s in stations.Value.Where(s => s.StationId > 0))
            list.Add(new Feature(Station(s.Key), s.Title, s.OwnerOnly));
        All = list;
    }

    public IReadOnlyList<Feature> All { get; }

    public bool IsOwner(string? username) => _owner.Length > 0 && Normalize(username) == _owner;

    /// <summary>Features this user may have (regardless of their own on/off settings).</summary>
    public IReadOnlyList<Feature> AvailableTo(string? username) =>
        All.Where(f => !f.OwnerOnly || IsOwner(username)).ToList();

    public bool Allows(Subscriber s, string featureId) =>
        All.FirstOrDefault(f => f.Id == featureId) is { } f && (!f.OwnerOnly || IsOwner(s.Username));

    private static string Normalize(string? username) => (username ?? "").Trim().TrimStart('@').ToLowerInvariant();
}
