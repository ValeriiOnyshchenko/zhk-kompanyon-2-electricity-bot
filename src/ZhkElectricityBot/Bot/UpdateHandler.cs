using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using ZhkElectricityBot.Storage;

namespace ZhkElectricityBot.Bot;

/// <summary>/start subscribes, /stop or blocking the bot unsubscribes; /status, /schedule, /settings.</summary>
public sealed class UpdateHandler(
    SubscriberStore store,
    Features features,
    StatusReport report,
    Broadcaster broadcaster,
    ILogger<UpdateHandler> log) : IUpdateHandler
{
    private const string TogglePrefix = "t:";

    public async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        switch (update)
        {
            case { Message: { Chat.Type: ChatType.Private, Text: { } text } msg }:
                await OnTextAsync(bot, msg, text.Trim(), ct);
                break;

            case { CallbackQuery: { Data: { } data, Message: { } msg } q } when data.StartsWith(TogglePrefix):
                await OnToggleAsync(bot, q, msg, data[TogglePrefix.Length..], ct);
                break;

            case { MyChatMember: { Chat.Type: ChatType.Private } change }
                when change.NewChatMember.Status == ChatMemberStatus.Kicked:
                await store.RemoveAsync(change.Chat.Id, ct);
                log.LogInformation("User {ChatId} blocked the bot — unsubscribed", change.Chat.Id);
                break;
        }
    }

    private async Task OnTextAsync(ITelegramBotClient bot, Message msg, string text, CancellationToken ct)
    {
        var command = text.Split(' ', '@')[0].ToLowerInvariant();
        var chatId = msg.Chat.Id;
        var username = msg.From?.Username;
        var available = features.AvailableTo(username);

        if (command == "/start")
        {
            await store.AddAsync(chatId, username, msg.From?.FirstName, ct);
            log.LogInformation("Subscribed {ChatId} (owner: {Owner})", chatId, features.IsOwner(username));
            await broadcaster.SendOneAsync(chatId, BotTexts.Welcome(available), ct);
            await broadcaster.SendOneAsync(chatId, await report.StatusAsync(available, ct), ct);
            return;
        }

        var subscriber = await store.GetAsync(chatId, ct);
        if (subscriber is null)
        {
            await bot.SendMessage(chatId, BotTexts.NotSubscribed, cancellationToken: ct);
            return;
        }
        if (subscriber.Username != username) await store.TouchAsync(chatId, username, msg.From?.FirstName, ct);

        switch (command)
        {
            case "/stop":
                await store.RemoveAsync(chatId, ct);
                log.LogInformation("Unsubscribed {ChatId}", chatId);
                await bot.SendMessage(chatId, BotTexts.Goodbye, cancellationToken: ct);
                break;

            case "/status":
                await broadcaster.SendOneAsync(chatId, await report.StatusAsync(available, ct), ct);
                break;

            case "/schedule":
                await broadcaster.SendOneAsync(chatId, report.Schedule(), ct);
                break;

            case "/settings":
                await bot.SendMessage(chatId, BotTexts.SettingsIntro,
                    replyMarkup: await SettingsKeyboardAsync(chatId, available, ct), cancellationToken: ct);
                break;

            default:
                await bot.SendMessage(chatId, BotTexts.Unknown, cancellationToken: ct);
                break;
        }
    }

    private async Task OnToggleAsync(ITelegramBotClient bot, CallbackQuery q, Message msg, string featureId, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var available = features.AvailableTo(q.From.Username);
        if (available.All(f => f.Id != featureId) || await store.GetAsync(chatId, ct) is null)
        {
            await bot.AnswerCallbackQuery(q.Id, "Недоступно", cancellationToken: ct);
            return;
        }

        var enabled = (await store.GetDisabledAsync(chatId, ct)).Contains(featureId); // was off → turn on
        await store.SetFeatureAsync(chatId, featureId, enabled, ct);
        await bot.AnswerCallbackQuery(q.Id, enabled ? "Увімкнено ✅" : "Вимкнено", cancellationToken: ct);
        await bot.EditMessageReplyMarkup(chatId, msg.MessageId, await SettingsKeyboardAsync(chatId, available, ct), cancellationToken: ct);
    }

    private async Task<InlineKeyboardMarkup> SettingsKeyboardAsync(long chatId, IReadOnlyList<Feature> available, CancellationToken ct)
    {
        var disabled = await store.GetDisabledAsync(chatId, ct);
        return new InlineKeyboardMarkup(available.Select(f => new[]
        {
            InlineKeyboardButton.WithCallbackData($"{(disabled.Contains(f.Id) ? "⬜" : "✅")} {f.Title}", TogglePrefix + f.Id),
        }));
    }

    public Task HandleErrorAsync(ITelegramBotClient bot, Exception ex, HandleErrorSource source, CancellationToken ct)
    {
        log.LogError(ex, "Telegram error ({Source})", source);
        return Task.Delay(TimeSpan.FromSeconds(5), ct); // a network outage must not spin the polling loop
    }
}
