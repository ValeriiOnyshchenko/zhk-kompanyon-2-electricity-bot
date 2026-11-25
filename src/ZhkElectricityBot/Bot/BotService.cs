using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace ZhkElectricityBot.Bot;

/// <summary>Long-polls Telegram for updates; syncs the profile texts and the command menu on startup.</summary>
public sealed class BotService(
    ITelegramBotClient bot,
    UpdateHandler handler,
    IOptions<ScheduleOptions> schedule,
    ILogger<BotService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var me = await bot.GetMe(ct);
        log.LogInformation("Started as @{Username}", me.Username);
        await SyncProfileAsync(ct);

        await bot.ReceiveAsync(handler, new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery, UpdateType.MyChatMember],
        }, ct);
    }

    /// <summary>Pushes name/descriptions/commands from <see cref="BotTexts"/>, only when they changed (rate-limited calls).</summary>
    private async Task SyncProfileAsync(CancellationToken ct)
    {
        try
        {
            if ((await bot.GetMyName(cancellationToken: ct)).Name != BotTexts.Name)
                await bot.SetMyName(BotTexts.Name, cancellationToken: ct);

            if ((await bot.GetMyShortDescription(cancellationToken: ct)).ShortDescription != BotTexts.ShortDescription)
                await bot.SetMyShortDescription(BotTexts.ShortDescription, cancellationToken: ct);

            var description = BotTexts.Description(schedule.Value);
            if ((await bot.GetMyDescription(cancellationToken: ct)).Description != description)
                await bot.SetMyDescription(description, cancellationToken: ct);

            var commands = BotTexts.Commands.Select(c => new BotCommand { Command = c.Command, Description = c.Description }).ToArray();
            var current = await bot.GetMyCommands(cancellationToken: ct);
            if (!current.Select(c => (c.Command, c.Description)).SequenceEqual(BotTexts.Commands))
                await bot.SetMyCommands(commands, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not sync bot profile texts");
        }
    }
}
