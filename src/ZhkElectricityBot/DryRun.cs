using Microsoft.Extensions.Options;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Stations;

namespace ZhkElectricityBot;

/// <summary><c>dotnet run -- --dry-run</c>: reads LOE and Deye once and prints the messages. Nothing is sent or stored.</summary>
public static class DryRun
{
    public static async Task RunAsync(IServiceProvider sp)
    {
        var clock = sp.GetRequiredService<LocalClock>();
        var schedule = sp.GetRequiredService<IOptions<ScheduleOptions>>().Value;
        var battery = sp.GetRequiredService<IOptions<BatteryOptions>>().Value;
        var stations = sp.GetRequiredService<IOptions<List<StationOptions>>>().Value;
        var deye = sp.GetRequiredService<DeyeClient>();
        var now = clock.UtcNow;

        Console.WriteLine($"=== LOE schedule, group {schedule.Group} ===");
        try
        {
            var days = await sp.GetRequiredService<LoeClient>().GetAsync(CancellationToken.None);
            foreach (var d in days)
            {
                Console.WriteLine($"{d.Date:yyyy-MM-dd}: [{d.Signature}] «{d.Line}» (as of {d.AsOf})");
                Console.WriteLine(Plain(BotTexts.SchedulePublished(d, schedule)));
                Console.WriteLine();
            }
            Console.WriteLine(Plain(BotTexts.ScheduleOverview(days, clock.Today, schedule)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LOE failed: {ex.Message}");
        }

        if (!deye.IsConfigured)
        {
            Console.WriteLine("\nDeye credentials are not set — skipping stations (dotnet user-secrets set \"Deye:AppId\" …).");
            return;
        }

        foreach (var s in stations.Where(s => s.StationId > 0))
        {
            Console.WriteLine($"\n=== Station {s.Key}: {s.Title} ===");
            try
            {
                var devices = await deye.GetStationDevicesAsync(s.StationId, CancellationToken.None);
                var inverters = devices.Where(d => d.Type.Equals("INVERTER", StringComparison.OrdinalIgnoreCase)).Select(d => d.Sn).ToList();
                var latest = await deye.GetStationLatestAsync(s.StationId, CancellationToken.None);
                var data = await deye.GetDeviceLatestAsync(inverters, CancellationToken.None);
                var r = StationReading.From(latest, data, battery, s.CapacityKwh);
                Console.WriteLine($"grid={r.GridOn} voltages=[{string.Join(", ", r.GridVoltages)}] soc={r.Soc} load={r.LoadW} battery={r.BatteryW} " +
                                  $"capacity={r.CapacityKwh:0.0} kWh updated={r.UpdatedUtc:u}");

                // What the outage messages would look like if the grid went down right now.

                var f = ForecastFromLoad(r, battery);
                Console.WriteLine(Plain(BotTexts.StationNow(s.Title, r, new StationState(), null, null, null, now, clock)));
                Console.WriteLine("\n--- if the grid went down now ---");
                Console.WriteLine(Plain(BotTexts.GridLost(s.Title, r, f, null, now, clock)));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Station {s.Key} failed: {ex.Message}");
            }
        }
    }

    private static Forecast? ForecastFromLoad(StationReading r, BatteryOptions o) =>
        r.Soc is { } soc && r.LoadW is > 10 && r.CapacityKwh is > 0
            ? new Forecast(TimeSpan.FromHours((soc - o.ReserveSocPercent) / 100 * r.CapacityKwh.Value / (r.LoadW.Value / 1000)), false, null, null)
            : null;

    private static string Plain(string html) =>
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));
}
