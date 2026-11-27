using Microsoft.Extensions.Options;
using Telegram.Bot;
using ZhkElectricityBot;
using ZhkElectricityBot.Bot;
using ZhkElectricityBot.Schedule;
using ZhkElectricityBot.Stations;
using ZhkElectricityBot.Storage;

var dryRun = args.Contains("--dry-run");
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

builder.Services.AddOptions<BotOptions>().Bind(config.GetSection(BotOptions.Section));
builder.Services.AddOptions<PollingOptions>().Bind(config.GetSection(PollingOptions.Section));
builder.Services.AddOptions<ScheduleOptions>().Bind(config.GetSection(ScheduleOptions.Section));
builder.Services.AddOptions<DeyeOptions>().Bind(config.GetSection(DeyeOptions.Section));
builder.Services.AddOptions<BatteryOptions>().Bind(config.GetSection(BatteryOptions.Section));
builder.Services.AddOptions<List<StationOptions>>().Bind(config.GetSection("Stations"));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LocalClock>();

builder.Services.AddHttpClient("web", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ZhkElectricityBot/1.0)");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("uk-UA,uk;q=0.9");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
builder.Services.AddHttpClient("deye", c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

builder.Services.AddSingleton<LoeClient>();
builder.Services.AddSingleton<DeyeClient>();

BotTexts.SafeMargin = TimeSpan.FromMinutes(config.GetValue("Battery:SafeMarginMinutes", 60));

if (dryRun)
{
    // Preview: read LOE and Deye live and print what the bot would say. No Telegram, no database.
    using var host = builder.Build();
    await DryRun.RunAsync(host.Services);
    return;
}

builder.Services.AddOptions<BotOptions>()
    .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Bot:Token is not set (use user-secrets locally or the Bot__Token env var).")
    .ValidateOnStart();

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<BotOptions>>().Value.Token));

builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<SubscriberStore>();
builder.Services.AddSingleton<Features>();
builder.Services.AddSingleton<Broadcaster>();
builder.Services.AddSingleton<ScheduleService>();
builder.Services.AddSingleton<StationNotifier>();
builder.Services.AddSingleton<StationService>();
builder.Services.AddSingleton<StatusReport>();
builder.Services.AddSingleton<UpdateHandler>();
builder.Services.AddHostedService<BotService>();
builder.Services.AddHostedService<PowerMonitorService>();

builder.Build().Run();
