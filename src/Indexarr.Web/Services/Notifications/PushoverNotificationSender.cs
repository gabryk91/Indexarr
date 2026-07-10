namespace Indexarr.Web.Services.Notifications;

public sealed class PushoverNotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;

    public PushoverNotificationSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NotificationSendResult> SendAsync(string userKey, string apiToken, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userKey) || string.IsNullOrWhiteSpace(apiToken))
        {
            return new(false, "Missing user key or API token.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(NotificationDispatchService));
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = apiToken,
                ["user"] = userKey,
                ["message"] = message
            });

            using var response = await client.PostAsync("https://api.pushover.net/1/messages.json", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new(true, "OK");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new(false, string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }
}
