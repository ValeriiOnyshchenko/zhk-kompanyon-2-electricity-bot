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

    public const string Name = "Бот-електрик ЖК Компаньйон ⚡";

    public const string ShortDescription =
        "Графік вимкнень світла і стан резервного живлення ЖК «Компаньйон»: вимкнення, батареї, прогнози.";

    public static string Description(ScheduleOptions o) =>
        "Добрий день! Я — бот-електрик ЖК «Компаньйон» 👷‍♂️⚡\n\n" +
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
        "Добрий день! 👷‍♂️⚡ Я — бот-електрик ЖК «Компаньйон», і тепер ви на звʼязку.\n\n" +
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
        $"🗓 <b>Графік вимкнень на {DayTitle(d.Date)}</b>, група {Html(o.Group)}\n\n" +
        SlotsText(d) + Footer(d, o);

    public static string ScheduleChanged(DaySchedule old, DaySchedule d, ScheduleOptions o) =>
        $"✏️ <b>Графік на {DayTitle(d.Date)} змінився</b>, група {Html(o.Group)}\n\n" +
        "Було:\n" + SlotsText(old) + "\n\n" +
        "Стало:\n" + SlotsText(d) + Footer(d, o);

    public static string OutageReminder(Outage outage, TimeSpan inTime, LocalClock clock, ScheduleOptions o) =>
        $"⏰ <b>Через {Duration(inTime)} вимкнуть світло</b>\n\n" +
        $"За графіком: {Time(clock.ToLocal(outage.Start))}–{EndTime(outage.End, clock)} ({Duration(outage.Length)}), група {Html(o.Group)}.\n" +
        "Встигніть зарядити телефони 🔌";

    public static string ScheduleOverview(IReadOnlyList<DaySchedule> days, DateOnly today, ScheduleOptions o)
    {
        var parts = new List<string>();
        foreach (var date in new[] { today, today.AddDays(1) })
        {
            var d = days.FirstOrDefault(x => x.Date == date);
            parts.Add($"<b>{DayTitle(date)}</b>\n" + (d is null ? "графік ще не опубліковано" : SlotsText(d)));
        }
        var asOf = days.Where(d => d.Date >= today).Select(d => d.AsOf).LastOrDefault(a => a.Length > 0);
        return $"🗓 Графік вимкнень, група {Html(o.Group)}\n\n" + string.Join("\n\n", parts) +
               (asOf is null ? "" : $"\n\n<i>Станом на {Html(asOf)}</i>") +
               $"\n<a href=\"{Html(o.SiteUrl)}\">Сайт Львівобленерго</a>";
    }

    private static string SlotsText(DaySchedule d)
    {
        if (d.Unrecognized) return Html(d.Line);
        if (d.Slots.Count == 0) return "✅ Вимкнень немає";
        var lines = d.Slots.Select(s => $"• {s} ({Duration(TimeSpan.FromMinutes(s.End - s.Start))})");
        var total = d.Slots.Count > 1 ? $"\nУсього без світла: {Duration(d.Total)}" : "";
        return string.Join("\n", lines) + total;
    }

    private static string Footer(DaySchedule d, ScheduleOptions o) =>
        (d.AsOf.Length > 0 ? $"\n\n<i>Станом на {Html(d.AsOf)}</i>" : "") +
        $"\n<a href=\"{Html(o.SiteUrl)}\">Сайт Львівобленерго</a>";

    private static string DayTitle(DateOnly d) => $"{Date(d)} ({DayName(d)})";

    /// <summary>Midnight as the end of an outage reads "24:00", like LOE writes it.</summary>
    public static string EndTime(DateTimeOffset end, LocalClock clock)
    {
        var local = clock.ToLocal(end);
        return local.TimeOfDay == TimeSpan.Zero ? "24:00" : Time(local);
    }

    // ---- Features 2 & 3: stations ----

    public static string GridLost(string title, StationReading r, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock) =>
        $"🔋 <b>{Html(title)}: зникло світло</b> — працюємо від батарей.\n\n" +
        Battery(r, null, null) +
        ForecastLine(f, now, clock) +
        PlannedLine(planned, now, clock) +
        Verdict(f, planned, now);

    public static string BatteryStatus(string title, StationReading r, StationState st, Forecast? f, Outage? planned, DateTimeOffset now, LocalClock clock) =>
        $"📊 <b>{Html(title)}</b> — {Duration(now - (st.OutageStart ?? now))} без світла\n\n" +
        Battery(r, st.SocAtStart, st.OutageStart is { } s ? now - s : null) +
        ForecastLine(f, now, clock) +
        PlannedLine(planned, now, clock) +
        Verdict(f, planned, now);

    public static string LowBattery(string title, int level, StationReading r, Forecast? f, DateTimeOffset now, LocalClock clock) =>
        $"{(level <= 10 ? "🚨" : "🪫")} <b>{Html(title)}: заряд впав до {level}%</b>\n\n" +
        Battery(r, null, null) +
        ForecastLine(f, now, clock) +
        (level <= 10 ? "\nЕкономте електроенергію — батареї майже розряджені." : "\nВарто економити електроенергію.");

    public static string GridBack(string title, StationReading r, TimeSpan? duration, double? used) =>
        $"💡 <b>{Html(title)}: світло повернулося!</b>\n\n" +
        (duration is { } d ? $"Без світла: {Duration(d)}\n" : "") +
        (r.Soc is { } soc ? $"Заряд батарей: {Percent(soc)}" + (used is > 0 ? $" (витрачено {Percent(used.Value)})" : "") + "\n" : "") +
        "Батареї знову заряджаються від мережі.";

    public static string StationOffline(string title, TimeSpan age, DateTimeOffset? last, LocalClock clock) =>
        $"📡 <b>{Html(title)}: немає даних від станції</b> уже {Duration(age)}" +
        (last is { } l ? $" (останні о {Time(clock.ToLocal(l))})" : "") +
        ".\nМожливо, зник інтернет у щитовій. Повідомлю, щойно звʼязок відновиться.";

    public static string StationOnline(string title) => $"📡 {Html(title)}: звʼязок зі станцією відновлено.";

    public static string StationNow(string title, StationReading? r, StationState st, Forecast? f, Outage? planned, Outage? next,
        DateTimeOffset now, LocalClock clock)
    {
        if (r is null) return $"<b>{Html(title)}</b>\n{NoData}";
        var head = r.GridOn switch
        {
            true => $"💡 <b>{Html(title)}</b>: світло є",
            false => $"🔋 <b>{Html(title)}</b>: на батареях" + (st.OutageStart is { } s ? $" уже {Duration(now - s)}" : ""),
            null => $"❔ <b>{Html(title)}</b>: стан мережі невідомий",
        };
        var body = Battery(r, r.GridOn == false ? st.SocAtStart : null, r.GridOn == false && st.OutageStart is { } s2 ? now - s2 : null);
        if (r.GridOn == false)
            body += ForecastLine(f, now, clock) + PlannedLine(planned, now, clock) + Verdict(f, planned, now);
        else if (next is not null)
            body += $"Наступне вимкнення за графіком: {Time(clock.ToLocal(next.Start))} ({When(next.Start, now, clock)})\n";
        var updated = r.UpdatedUtc is { } u ? $"\n<i>Дані станції о {Time(clock.ToLocal(u))}</i>" : "";
        return head + "\n" + body.TrimEnd('\n') + updated;
    }

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
        if (f.Left <= TimeSpan.Zero) return "Автономність: батареї розряджені\n";
        var basis = f.FromDischarge && f.SocDrop is { } drop && f.Over is { } over
            ? $"за фактичним розрядом: −{Percent(drop)} за {Duration(over)}"
            : "за поточним навантаженням";
        return f.Left > TimeSpan.FromHours(48)
            ? $"Автономність: понад 2 доби ({basis})\n"
            : $"Автономність: ~{Duration(f.Left)}, до ~{Time(clock.ToLocal(now + f.Left))} ({basis})\n";
    }

    private static string PlannedLine(Outage? planned, DateTimeOffset now, LocalClock clock)
    {
        if (planned is null) return "Вимкнення поза графіком — коли повернеться світло, невідомо.\n";
        var end = EndTime(planned.End, clock);
        return planned.End > now
            ? $"Світло за графіком: о {end} (через {Duration(planned.End - now)})\n"
            : $"За графіком світло мало зʼявитися о {end} — вмикання може тривати до години.\n";
    }

    private static string Verdict(Forecast? f, Outage? planned, DateTimeOffset now)
    {
        if (f is null || planned is null || planned.End <= now) return "";
        return now + f.Left >= planned.End
            ? "✅ Заряду має вистачити до кінця вимкнення."
            : "⚠️ Заряду може не вистачити до кінця вимкнення — економте електроенергію.";
    }

    private static string When(DateTimeOffset t, DateTimeOffset now, LocalClock clock)
    {
        var day = DateOnly.FromDateTime(clock.ToLocal(t).DateTime);
        var today = DateOnly.FromDateTime(clock.ToLocal(now).DateTime);
        return day == today ? $"через {Duration(t - now)}" : day == today.AddDays(1) ? "завтра" : Date(day);
    }
}
