namespace ZhkElectricityBot;

public sealed class BotOptions
{
    public const string Section = "Bot";

    /// <summary>Telegram bot token from @BotFather. Secret: user-secrets locally, BOT_TOKEN in CI.</summary>
    public string Token { get; set; } = "";

    /// <summary>Telegram username (with or without @) of the owner — the only account that sees owner-only stations. Secret.</summary>
    public string OwnerUsername { get; set; } = "";

    public string DatabasePath { get; set; } = "db/bot.db";

    /// <summary>All times in messages and in the LOE schedule are in this zone.</summary>
    public string TimeZone { get; set; } = "Europe/Kyiv";
}

public sealed class PollingOptions
{
    public const string Section = "Polling";

    /// <summary>Normal check interval. Checks are aligned to the clock: 5 → xx:00, xx:05, xx:10…</summary>
    public int IdleIntervalMinutes { get; set; } = 5;

    /// <summary>Interval around outages (scheduled or real): 1 → at the start of every minute.</summary>
    public int ActiveIntervalMinutes { get; set; } = 1;

    public int ActiveBeforeOutageMinutes { get; set; } = 60;
    public int ActiveAfterOutageMinutes { get; set; } = 60;
}

public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    public bool Enabled { get; set; } = true;

    /// <summary>JSON endpoint behind poweron.loe.lviv.ua (items "Today"/"Tomorrow" with the schedule text).</summary>
    public string Url { get; set; } = "https://api.loe.lviv.ua/api/menus?page=1&type=photo-grafic";

    public string SiteUrl { get; set; } = "https://poweron.loe.lviv.ua/";

    /// <summary>Outage queue, e.g. "3.1".</summary>
    public string Group { get; set; } = "3.1";

    /// <summary>Remind this many minutes before a scheduled outage.</summary>
    public int ReminderMinutes { get; set; } = 15;
}

public sealed class DeyeOptions
{
    public const string Section = "Deye";

    public string BaseUrl { get; set; } = "https://eu1-developer.deyecloud.com/v1.0";
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>Deye Cloud password in plain text; it is SHA-256 hashed before it is sent.</summary>
    public string Password { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AppId) && !string.IsNullOrWhiteSpace(AppSecret) &&
        !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

public sealed class BatteryOptions
{
    public const string Section = "Battery";

    /// <summary>During an outage, battery status is sent every N minutes after it started.</summary>
    public int StatusIntervalMinutes { get; set; } = 60;

    /// <summary>One warning per level when the charge falls to it during an outage.</summary>
    public List<int> WarnSocLevels { get; set; } = [30, 10];

    /// <summary>The forecast counts time until the charge reaches this level (inverter cut-off). 0 = until empty.</summary>
    public int ReserveSocPercent { get; set; } = 0;

    /// <summary>Grid is considered present when every grid phase voltage is above this (V).</summary>
    public double GridVoltageThreshold { get; set; } = 150;

    /// <summary>The forecast uses the real discharge rate once the outage lasts this long…</summary>
    public int MinForecastMinutes { get; set; } = 15;

    /// <summary>…and the charge dropped at least this many percent. Before that it uses the current load.</summary>
    public int MinForecastSocDrop { get; set; } = 2;

    /// <summary>If the cloud has no fresh data for this long, the station is reported as offline (once).</summary>
    public int StaleDataMinutes { get; set; } = 30;

    /// <summary>LiFePO4: 3.2 V per cell. Used to turn the rated Ah from the API into kWh.</summary>
    public double NominalCellVoltage { get; set; } = 3.2;
}

public sealed class StationOptions
{
    /// <summary>Stable id used in the database and in feature settings ("complex", "home").</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Deye Cloud station id. Secret (DEYE_*_STATION_ID). 0 = station disabled.</summary>
    public long StationId { get; set; }

    /// <summary>Only the owner (Bot:OwnerUsername) gets this station.</summary>
    public bool OwnerOnly { get; set; }

    /// <summary>Battery capacity override, kWh. 0 = read from the API (rated Ah × nominal voltage).</summary>
    public double CapacityKwh { get; set; }
}
