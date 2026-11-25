using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace ZhkElectricityBot.Schedule;

/// <summary>
/// Parses the text LOE publishes in "rawHtml", e.g.
/// <code>
/// Графік погодинних відключень на 26.11.2026
/// Інформація станом на 22:11 25.11.2026
/// Група 3.1. Електроенергії немає з 13:30 до 17:00.
/// Група 5.2. Електроенергії немає з 08:00 до 10:00, з 13:30 до 17:00.
/// Група 6.2. Електроенергія є.
/// </code>
/// </summary>
public static partial class LoeParser
{
    public static DaySchedule? Parse(string? rawHtml, string group)
    {
        if (string.IsNullOrWhiteSpace(rawHtml)) return null;
        var lines = ToLines(rawHtml);

        var date = lines.Select(l => DateRegex().Match(l)).FirstOrDefault(m => m.Success);
        if (date is null || !DateOnly.TryParseExact(date.Groups[1].Value, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
            return null;

        var asOf = lines.Select(l => AsOfRegex().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value ?? "";

        var prefix = new Regex(@"^\s*Група\s*" + Regex.Escape(group) + @"\s*\.\s*(.+)$", RegexOptions.IgnoreCase);
        var line = lines.Select(l => prefix.Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value.Trim();
        if (line is null) return null;

        var result = new DaySchedule { Date = day, AsOf = asOf, Line = line };
        foreach (Match m in SlotRegex().Matches(line))
        {
            var start = ToMinutes(m.Groups[1].Value, m.Groups[2].Value);
            var end = ToMinutes(m.Groups[3].Value, m.Groups[4].Value);
            if (end == 0) end = 1440; // "до 00:00" = end of the day
            if (end > start) result.Slots.Add(new Slot(start, end));
        }
        result.Slots = result.Slots.OrderBy(s => s.Start).ToList();
        result.Unrecognized = result.Slots.Count == 0 && !NoOutageRegex().IsMatch(line);
        return result;
    }

    private static List<string> ToLines(string html)
    {
        var text = BreakRegex().Replace(html, "\n");
        text = TagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
        return text.Split('\n').Select(l => SpaceRegex().Replace(l, " ").Trim()).Where(l => l.Length > 0).ToList();
    }

    private static int ToMinutes(string h, string m) => Math.Min(1440, int.Parse(h) * 60 + int.Parse(m));

    [GeneratedRegex(@"відключень\s+на\s+(\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"станом\s+на\s+(\d{1,2}:\d{2}\s+\d{2}\.\d{2}\.\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AsOfRegex();

    [GeneratedRegex(@"з\s*(\d{1,2}):(\d{2})\s*до\s*(\d{1,2}):(\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SlotRegex();

    [GeneratedRegex(@"Електроенергія\s+є", RegexOptions.IgnoreCase)]
    private static partial Regex NoOutageRegex();

    [GeneratedRegex(@"<\s*br\s*/?\s*>|</\s*(p|div|li|h\d)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}
