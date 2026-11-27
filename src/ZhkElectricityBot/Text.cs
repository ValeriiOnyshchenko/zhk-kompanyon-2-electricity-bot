using System.Globalization;
using System.Net;

namespace ZhkElectricityBot;

/// <summary>Small Ukrainian formatting helpers.</summary>
public static class Text
{
    private static readonly string[] Days = ["неділя", "понеділок", "вівторок", "середа", "четвер", "пʼятниця", "субота"];

    public static string Html(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static string Time(DateTimeOffset t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateOnly d) => d.ToString("dd.MM", CultureInfo.InvariantCulture);

    public static string DayName(DateOnly d) => Days[(int)d.DayOfWeek];

    public static string Minutes(int m) => m >= 1440 ? "24:00" : $"{m / 60:00}:{m % 60:00}";

    /// <summary>"3 год 30 хв", "45 хв", "менше хвилини". From an hour up — rounded to 5 min ("3 год 1 хв" → "3 год").</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        var total = (int)Math.Round(d.TotalMinutes);
        if (total >= 60) total = (int)Math.Round(total / 5.0) * 5;
        if (total < 1) return "менше хвилини";
        var h = total / 60;
        var m = total % 60;
        return h == 0 ? $"{m} хв" : m == 0 ? $"{h} год" : $"{h} год {m} хв";
    }

    public static string Kw(double watts) =>
        Math.Abs(watts) < 1000
            ? $"{Math.Round(watts):0} Вт"
            : (watts / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " кВт";

    public static string Percent(double v) => $"{Math.Round(v):0}%";
}
