using System.Net.Http.Json;

namespace Indexarr.Web.Services.Notifications;

public sealed class TelegramNotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TelegramNotificationSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NotificationSendResult> SendAsync(string botToken, string chatId, string message, CancellationToken cancellationToken = default)
    {
        botToken = botToken?.Trim() ?? string.Empty;
        chatId = chatId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatId))
        {
            return new(false, "Missing bot token or chat id.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(NotificationDispatchService));
            using var response = await client.PostAsJsonAsync(
                $"https://api.telegram.org/bot{botToken}/sendMessage",
                new { chat_id = chatId, text = message },
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new(true, "OK");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new(false, "Telegram bot token non trovato. Verifica il token copiato da @BotFather e non includere il prefisso 'bot'.");
            }

            return new(false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }
}
