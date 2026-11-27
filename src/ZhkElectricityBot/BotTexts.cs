using ZhkElectricityBot.Bot;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Stations;
using static ZhkElectricityBot.Text;

namespace ZhkElectricityBot;

/// <summary>
/// Every text the user can see (Ukrainian only, Telegram HTML).
/// Profile texts (name/descriptions) are synced to Telegram on startup.
/// </summary>
public static class BotTexts
{
    // ---- Telegram profile (limits: name 64, short description 120, description 512 chars) ----

    public const string Name = "Пан Амперій ⚡ електрик ЖК Компаньйон";

    public const string ShortDescription =
        "Графік вимкнень світла і стан резервного живлення ЖК «Компаньйон»: вимкнення, батареї, прогнози.";

    public static string Description(ScheduleOptions o) =>
        "Добрий день! Я — Пан Амперій, електрик ЖК «Компаньйон» 👷‍♂️⚡\n\n" +
        "Стежу за світлом у ЖК «Компаньйон»:\n" +
        $"• графік погодинних вимкнень для групи {o.Group} — повідомлю, коли він зʼявиться чи зміниться, " +
        $"і нагадаю за {o.ReminderMinutes} хв до вимкнення;\n" +
        "• резервне живлення ЖК — скажу, коли будинок перейде на батареї і коли повернеться світло, " +
        "а під час вимкнення щогодини розповім про заряд і скільки ще протримаємось.\n\n" +
        "Натисніть «Почати», щоб підписатися.";

    // ---- Commands ----

    public static readonly (string Command, string Description)[] Commands =
    [
        ("status", "Стан живлення зараз"),
        ("schedule", "Графік вимкнень на сьогодні і завтра"),
        ("settings", "Які сповіщення отримувати"),
        ("stop", "Відписатися"),
    ];

    public static string Welcome(IReadOnlyList<Feature> features) =>
        "Добрий день! 👷‍♂️⚡ Я — Пан Амперій, електрик ЖК «Компаньйон», і тепер ви на звʼязку.\n\n" +
        "Ось що я для вас робитиму:\n" +
        string.Join("\n", features.Select(f => "• " + Html(FeatureLong(f)))) + "\n\n" +
        "Команди:\n" +
        "/status — що зі світлом і батареями зараз\n" +
        "/schedule — графік вимкнень\n" +
        "/settings — увімкнути чи вимкнути окремі сповіщення\n" +
        "/stop — відписатися";

    private static string FeatureLong(Feature f) => f.Id == Features.Schedule
        ? $"{f.Title}: новий графік, зміни в ньому і нагадування перед вимкненням"
        : $"{f.Title}: перехід на батареї і повернення світла, заряд щогодини під час вимкнення, попередження про низький заряд";

    public const string Goodbye =
        "Ви відписалися. Якщо знову захочете знати про світло — натисніть /start. Бережіть себе! 👋";

    public const string NotSubscribed = "Ви ще не підписані — натисніть /start 🙂";

    public const string Unknown =
        "Я електрик простий — розмовляю командами 🙂\n\n/status — стан зараз\n/schedule — графік\n/settings — сповіщення\n/stop — відписатися";

    public const string SettingsIntro = "Оберіть, які сповіщення надсилати (натисніть, щоб увімкнути чи вимкнути):";

    public const string NoData = "Поки немає даних — спробуйте за хвилину.";

    // ---- Feature 1: schedule ----

    public static string SchedulePublished(DaySchedule d, ScheduleOptions o) =>
        $"🗓 <b>Графік вимкнень на {DayTitle(d.Date)}</b>, група {Html(o.Group)}\n\n" + SlotsText(d);

    public static string ScheduleChanged(DaySchedule old, DaySchedule d, ScheduleOptions o) =>
        $"✏️ <b>Графік на {DayTitle(d.Date)} змінився</b>, група {Html(o.Group)}\n\n" +
        "Було:\n" + SlotsText(old) + "\n\n" +
        "Стало:\n" + SlotsText(d);

    public static string OutageReminder(Outage outage, TimeSpan inTime, LocalClock clock, ScheduleOptions o) =>
        $"⏰ <b>Через {Duration(inTime)} вимкнуть світло</b>\n\n" +
        $"За графіком: {Time(clock.ToLocal(outage.Start))}–{EndTime(outage.End, clock)} ({Duration(outage.Length)}), група {Html(o.Group)}";

    public static string ScheduleOverview(IReadOnlyList<DaySchedule> days, DateOnly today, ScheduleOptions o)
    {
        var parts = new List<string>();
        foreach (var date in new[] { today, today.AddDays(1) })
        {
            var d = days.FirstOrDefault(x => x.Date == date);
            parts.Add($"<b>{DayTitle(date)}</b>\n" + (d is null ? "графік ще не опубліковано" : SlotsText(d)));
        }
        return $"🗓 <b>Графік вимкнень</b>, група {Html(o.Group)}\n\n" + string.Join("\n\n", parts);
    }

    private static string SlotsText(DaySchedule d)
    {
        if (d.Unrecognized) return "⚠️ " + Html(d.Line);
        if (d.Slots.Count == 0) return "✅ Вимкнень немає";
        var lines = d.Slots.Select(s => $"• {s} ({Duration(TimeSpan.FromMinutes(s.End - s.Start))})");
        var total = d.Slots.Count > 1 ? $"\nℹ️ Усього без світла: {Duration(d.Total)}" : "";
        return string.Join("\n", lines) + total;
    }

    private static string DayTitle(DateOnly d) => $"{Date(d)} ({DayName(d)})";

    /// <summary>Midnight as the end of an outage reads "24:00", like LOE writes it.</summary>
    public static string EndTime(DateTimeOffset end, LocalClock clock)
    {
        var local = clock.ToLocal(end);
        return local.TimeOfDay == TimeSpan.Zero ? "24:00" : Time(local);
    }

    // ---- Features 2 & 3: stations ----
    // A station message = header line "🏠 Квартира" + body (first body line is the event).
    // Several stations' bodies can be joined into one message (see StationNotifier).

    public static string Section(StationOptions s, string body) => $"{s.Icon} <b>{Html(s.Title)}</b>\n{body}";

    public static string GridLost(StationReading r, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock) =>
        "🔋 <b>Зникло світло</b> — працюємо від батарей\n" +
        Battery(r, null, null) +
        ForecastLine(f, now, clock) +
        PlannedLine(planned, now, clock) +
        Verdict(f, planned, now);

    public static string BatteryStatus(StationReading r, StationState st, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock) =>
        $"📊 <b>{Duration(now - (st.OutageStart ?? now))} без світла</b>\n" +
        Battery(r, st.SocAtStart, st.OutageStart is { } s ? now - s : null) +
        ForecastLine(f, now, clock) +
        PlannedLine(planned, now, clock) +
        Verdict(f, planned, now);

    /// <summary>
    /// If the charge may not / will not last until power returns, that verdict is the last line;
    /// otherwise the verdict (if any) and a plain tip.
    /// </summary>
    public static string LowBattery(int level, StationReading r, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock)
    {
        var verdict = Verdict(f, planned, now);
        var tail = verdict.StartsWith("✅") || verdict.Length == 0
            ? (verdict.Length > 0 ? verdict + "\n" : "") + (level <= 10 ? "🚨 Батареї майже розряджені" : "⚠️ Варто економити електроенергію")
            : verdict;
        return $"🪫 <b>Заряд впав до {level}%</b>\n" +
               Battery(r, null, null) +
               ForecastLine(f, now, clock) +
               PlannedLine(planned, now, clock) +
               tail;
    }

    public static string GridBack(StationReading r, TimeSpan? duration, double? used) =>
        "💡 <b>Світло повернулося!</b>\n" +
        (duration is { } d ? $"Без світла: {Duration(d)}\n" : "") +
        (r.Soc is { } soc ? $"Заряд: {Percent(soc)}" + (used is >= 1 ? $" (витрачено {Percent(used.Value)})" : "") + "\n" : "") +
        "✅ Батареї заряджаються від мережі";

    public static string StationOffline(TimeSpan age, DateTimeOffset? last, LocalClock clock) =>
        $"📡 <b>Немає даних від станції</b> уже {Duration(age)}" +
        (last is { } l ? $" (останні о {Time(clock.ToLocal(l))})" : "") +
        "\n⚠️ Можливо, зник інтернет";

    public const string StationOnline = "📡 Звʼязок зі станцією відновлено";

    /// <summary>/status: what the inverter does now (not the outage schedule).</summary>
    public static string StationNow(StationReading r, StationState st, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock)
    {
        var head = r.GridOn switch
        {
            true => "💡 <b>Живлення від мережі</b>",
            false => "🔋 <b>Живлення від батарей</b>" + (st.OutageStart is { } s ? $" уже {Duration(now - s)}" : ""),
            null => "❔ <b>Стан мережі невідомий</b>",
        };
        var body = Battery(r, r.GridOn == false ? st.SocAtStart : null, r.GridOn == false && st.OutageStart is { } s2 ? now - s2 : null);
        if (r.GridOn == false) body += ForecastLine(f, now, clock) + PlannedLine(planned, now, clock) + Verdict(f, planned, now);
        else if (r.BatteryW is < -30 and var charge) body += $"Батареї заряджаються: {Kw(-charge)}\n";
        var stale = r.UpdatedUtc is { } u && now - u > TimeSpan.FromMinutes(10)
            ? $"\n<i>Дані станції о {Time(clock.ToLocal(u))}</i>" : "";
        return head + "\n" + body.TrimEnd('\n') + stale;
    }

    /// <summary>/status when Deye Cloud can't be read for this station.</summary>
    public static string StationUnreachable(DateTimeOffset? last, LocalClock clock) =>
        "⚠️ <b>Немає звʼязку з інвертором</b>" +
        (last is { } l ? $"\nОстанні дані о {Time(clock.ToLocal(l))}" : "");

    private static string Battery(StationReading r, double? socAtStart, TimeSpan? elapsed)
    {
        var soc = r.Soc is { } s
            ? $"Заряд: <b>{Percent(s)}</b>" +
              (socAtStart is { } s0 && elapsed is { } e && s0 - s >= 1 ? $" (−{Percent(s0 - s)} за {Duration(e)})" : "") + "\n"
            : "";
        var load = r.LoadW is { } l ? $"Навантаження: {Kw(l)}\n" : "";
        return soc + load;
    }

    private static string ForecastLine(Forecast? f, DateTimeOffset now, LocalClock clock)
    {
        if (f is null) return "";
        if (f.Left <= TimeSpan.Zero) return "Автономність: заряд на межі вимкнення інвертора\n";
        return f.Left > TimeSpan.FromHours(48)
            ? "Автономність: понад 2 доби\n"
            : $"Автономність: ~{Duration(f.Left)}, до ~{Time(clock.ToLocal(now + f.Left))}\n";
    }

    private static string PlannedLine(Outage? planned, DateTimeOffset now, LocalClock clock)
    {
        if (planned is null) return "⚠️ Вимкнення поза графіком\n";
        var end = Time(clock.ToLocal(planned.End)); // "о 00:00", not "о 24:00"
        return planned.End > now
            ? $"Світло за графіком: о {end} (через {Duration(planned.End - now)})\n"
            : $"⚠️ За графіком світло мало зʼявитися о {end}\n";
    }

    /// <summary>Margin = when the batteries run out − when power returns per schedule.</summary>
    private static string Verdict(Forecast? f, Outage? planned, DateTimeOffset now)
    {
        if (f is null || planned is null || planned.End <= now) return "";
        var margin = now + f.Left - planned.End;
        return margin > SafeMargin ? "✅ Заряду вистачає до кінця вимкнення"
             : margin > TimeSpan.Zero ? "⚠️ Заряду може не вистачити до кінця вимкнення"
             : "🚨 Заряду не вистачає до кінця вимкнення";
    }

    /// <summary>Set from Battery:SafeMarginMinutes at startup.</summary>
    public static TimeSpan SafeMargin { get; set; } = TimeSpan.FromHours(1);

    private static string VerdictLine(Forecast? f, Outage? planned, DateTimeOffset now) =>
        Verdict(f, planned, now) is { Length: > 0 } v ? v + "\n" : "";

    private static string When(DateTimeOffset t, DateTimeOffset now, LocalClock clock)
    {
        var day = DateOnly.FromDateTime(clock.ToLocal(t).DateTime);
        var today = DateOnly.FromDateTime(clock.ToLocal(now).DateTime);
        return day == today ? $"через {Duration(t - now)}" : day == today.AddDays(1) ? "завтра" : Date(day);
    }
}
