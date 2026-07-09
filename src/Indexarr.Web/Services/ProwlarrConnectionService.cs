using System.Net.Http.Headers;
using Indexarr.Web.Localization;

namespace Indexarr.Web.Services;

public sealed class ProwlarrConnectionService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ProwlarrConnectionService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProwlarrConnectionResult> TestAsync(string url, string apiKey, string? language = null, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
        {
            return new(false, UiTextCatalog.Get(language, "InvalidProwlarrUrl"));
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(ProwlarrConnectionService));
            client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Clear();

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/status");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new(true, "OK");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = string.IsNullOrWhiteSpace(body)
                ? $"HTTP {(int)response.StatusCode}"
                : $"HTTP {(int)response.StatusCode}: {body}";

            return new(false, message);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }
}

public sealed record ProwlarrConnectionResult(bool Success, string Message);
