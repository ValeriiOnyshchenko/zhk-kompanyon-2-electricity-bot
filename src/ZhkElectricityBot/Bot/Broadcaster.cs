using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using ZhkElectricityBot.Storage;

namespace ZhkElectricityBot.Bot;

/// <summary>Sends a feature's message to every subscriber who has that feature (and didn't switch it off).</summary>
public sealed class Broadcaster(
    ITelegramBotClient bot,
    SubscriberStore store,
    Features features,
    ILogger<Broadcaster> log)
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(40); // ~25 msg/sec overall

    public async Task<int> SendAsync(string featureId, string html, CancellationToken ct = default)
    {
        var disabled = await store.GetAllDisabledAsync(ct);
        var chats = (await store.GetAllAsync(ct))
            .Where(s => features.Allows(s, featureId) && !disabled.Contains((s.ChatId, featureId)))
            .ToList();

        var sent = 0;
        foreach (var s in chats)
        {
            if (await SendOneAsync(s.ChatId, html, ct)) sent++;
            await Task.Delay(Delay, ct);
        }
        log.LogInformation("[{Feature}] sent to {Sent}/{Total}: {Preview}", featureId, sent, chats.Count,
            html.Split('\n')[0]);
        return sent;
    }

    /// <summary>Every subscriber with the features they may have and didn't switch off.</summary>
    public async Task<List<(long ChatId, HashSet<string> Features)>> RecipientsAsync(CancellationToken ct = default)
    {
        var disabled = await store.GetAllDisabledAsync(ct);
        return (await store.GetAllAsync(ct))
            .Select(s => (s.ChatId, features.AvailableTo(s.Username).Select(f => f.Id)
                .Where(id => !disabled.Contains((s.ChatId, id))).ToHashSet()))
            .ToList();
    }

    public async Task<bool> SendOneAsync(long chatId, string html, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await bot.SendMessage(chatId, html, ParseMode.Html,
                    linkPreviewOptions: new LinkPreviewOptions { IsDisabled = true }, cancellationToken: ct);
                return true;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 403)
            {
                await store.RemoveAsync(chatId, ct); // blocked the bot or deleted the account
                log.LogInformation("Removed {ChatId}: {Reason}", chatId, ex.Message);
                return false;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429 && attempt < 3)
            {
                var wait = ex.Parameters?.RetryAfter ?? 5;
                log.LogWarning("Rate limited, waiting {Seconds}s", wait);
                await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Failed to send to {ChatId}", chatId);
                return false;
            }
        }
    }
}
