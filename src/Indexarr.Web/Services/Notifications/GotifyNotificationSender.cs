using System.Net.Http.Json;

namespace Indexarr.Web.Services.Notifications;

public sealed class GotifyNotificationSender
{
    private readonly IHttpClientFactory _httpClientFactory;

    public GotifyNotificationSender(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<NotificationSendResult> SendAsync(string serverUrl, string appToken, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(appToken))
        {
            return new(false, "Missing server URL or app token.");
        }

        if (!Uri.TryCreate(serverUrl.TrimEnd('/') + "/message", UriKind.Absolute, out var endpoint))
        {
            return new(false, "Invalid Gotify server URL.");
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(NotificationDispatchService));
            var builder = new UriBuilder(endpoint) { Query = $"token={Uri.EscapeDataString(appToken)}" };
            using var response = await client.PostAsJsonAsync(
                builder.Uri,
                new { title = "Indexarr", message },
                cancellationToken);

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
