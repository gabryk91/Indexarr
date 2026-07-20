using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Indexarr.Web.Localization;
using Indexarr.Web.Models;

namespace Indexarr.Web.Services;

public sealed class ProwlarrApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;

    public ProwlarrApiClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ProwlarrSystemStatusDto> GetSystemStatusAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/status");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (await JsonSerializer.DeserializeAsync<ProwlarrSystemStatusDto>(stream, JsonOptions, cancellationToken)) ?? new ProwlarrSystemStatusDto();
    }

    public async Task<IReadOnlyList<ProwlarrIndexerDto>> GetIndexersAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return (await JsonSerializer.DeserializeAsync<List<ProwlarrIndexerDto>>(stream, JsonOptions, cancellationToken)) ?? new List<ProwlarrIndexerDto>();
    }

    public async Task<string> GetIndexersPayloadAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<ProwlarrIndexerRecord> GetIndexerRecordAsync(SetupDraft configuration, int indexerId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: true);
        var payload = await GetIndexerPayloadAsync(client, indexerId, cancellationToken);
        var node = JsonNode.Parse(payload)?.AsObject() ?? new JsonObject();

        return new ProwlarrIndexerRecord
        {
            Id = node["id"]?.GetValue<int>() ?? indexerId,
            Name = node["name"]?.GetValue<string>() ?? $"Indexer {indexerId}",
            Enabled = node["enable"]?.GetValue<bool>() ?? false,
            Protocol = node["protocol"]?.GetValue<string>() ?? "-",
            Implementation = node["implementation"]?.GetValue<string>() ?? "-",
            PayloadJson = payload
        };
    }

    public async Task<ProwlarrIndexerUpdateResult> UpdateIndexerPayloadAsync(SetupDraft configuration, int indexerId, JsonObject payload, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: true);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/indexer/{indexerId}")
        {
            Content = new StringContent(payload.ToJsonString())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ProwlarrIndexerUpdateResult
        {
            Success = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode ? T(configuration.Language, "IndexerUpdated") : BuildError(response.StatusCode, content),
            ResponseJson = content
        };
    }

    public async Task<ProwlarrIndexerUpdateResult> SetIndexerEnabledAsync(SetupDraft configuration, int indexerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: true);
        var payload = await GetIndexerPayloadAsync(client, indexerId, cancellationToken);
        var node = JsonNode.Parse(payload)?.AsObject();
        if (node is null)
        {
            return new ProwlarrIndexerUpdateResult { Success = false, Message = T(configuration.Language, "InvalidIndexerPayload") };
        }

        node["enable"] = enabled;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/indexer/{indexerId}")
        {
            Content = new StringContent(node.ToJsonString())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ProwlarrIndexerUpdateResult
        {
            Success = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode
                ? T(configuration.Language, enabled ? "IndexerEnabled" : "IndexerDisabled")
                : BuildError(response.StatusCode, content),
            ResponseJson = content
        };
    }

    public async Task<JsonArray> GetIndexerSchemasAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer/schema");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(content)?.AsArray() ?? [];
    }

    public async Task<IReadOnlyList<ProwlarrAppProfileOption>> GetAppProfilesAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/appprofile");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var profiles = await JsonSerializer.DeserializeAsync<List<ProwlarrAppProfileOption>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return profiles
            .Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<ProwlarrDownloadClientOption>> GetDownloadClientsAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/downloadclient");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var clients = await JsonSerializer.DeserializeAsync<List<ProwlarrDownloadClientOption>>(stream, JsonOptions, cancellationToken)
            ?? [];

        return clients
            .Where(x => x.Id > 0 && !string.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.Enable)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<ProwlarrCategoryOption>> GetIndexerCategoriesAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var categories = new Dictionary<int, ProwlarrCategoryOption>();

        try
        {
            await TryAppendDefaultIndexerCategoriesAsync(configuration, categories, cancellationToken);
        }
        catch
        {
        }

        if (categories.Count == 0)
        {
            try
            {
                var schemas = await GetIndexerSchemasAsync(configuration, cancellationToken);
                foreach (var schema in schemas.OfType<JsonObject>())
                {
                    if (schema["categories"] is JsonArray schemaCategories)
                    {
                        AppendCategories(schemaCategories, 0, categories);
                    }

                    if (schema["capabilities"]?["categories"] is JsonArray capabilityCategories)
                    {
                        AppendCategories(capabilityCategories, 0, categories);
                    }

                    if (schema["fields"] is JsonArray fields)
                    {
                        AppendCategoriesFromFields(fields, categories);
                    }
                }
            }
            catch
            {
            }
        }

        if (categories.Count == 0)
        {
            try
            {
                await TryAppendConfiguredIndexerCategoriesAsync(configuration, categories, cancellationToken);
            }
            catch
            {
            }
        }

        return categories.Values
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public async Task<IReadOnlyList<ProwlarrTagOption>> GetTagsAsync(SetupDraft configuration, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/tag");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = new List<ProwlarrTagOption>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProperty) ? idProperty.GetInt32() : 0;
            var label = item.TryGetProperty("label", out var labelProperty)
                ? labelProperty.GetString()
                : item.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString()
                    : null;
            if (id > 0 && !string.IsNullOrWhiteSpace(label))
            {
                result.Add(new ProwlarrTagOption
                {
                    Id = id,
                    Label = label
                });
            }
        }

        return result
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id)
            .ToList();
    }

    public async Task<ProwlarrIndexerUpdateResult> CreateIndexerAsync(SetupDraft configuration, JsonObject schema, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: true);
        var payload = JsonNode.Parse(schema.ToJsonString())?.AsObject() ?? new JsonObject();
        payload["enable"] ??= true;
        payload.Remove("id");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/indexer")
        {
            Content = new StringContent(payload.ToJsonString())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ProwlarrIndexerUpdateResult
        {
            Success = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode ? T(configuration.Language, "IndexerCreated") : BuildError(response.StatusCode, content),
            ResponseJson = content
        };
    }

    public async Task<ProwlarrIndexerUpdateResult> DeleteIndexerAsync(SetupDraft configuration, int indexerId, CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: true);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/indexer/{indexerId}");

        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ProwlarrIndexerUpdateResult
        {
            Success = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode ? T(configuration.Language, "IndexerDeleted") : BuildError(response.StatusCode, content),
            ResponseJson = content
        };
    }

    public async Task<IndexerHealthViewModel> TestIndexerAsync(
        SetupDraft configuration,
        ProwlarrIndexerDto indexer,
        bool testWhenDisabled = false,
        CancellationToken cancellationToken = default)
    {
        if (!indexer.Enable && !testWhenDisabled)
        {
            return new IndexerHealthViewModel
            {
                Id = indexer.Id,
                Name = indexer.Name ?? $"Indexer {indexer.Id}",
                Enabled = false,
                Protocol = indexer.Protocol ?? "-",
                Implementation = indexer.Implementation ?? "-",
                Result = "Disabled",
                Error = string.Empty,
                LatencyMs = 0
            };
        }

        var client = CreateClient(configuration, isMutation: false);
        var body = await GetIndexerPayloadAsync(client, indexer.Id, cancellationToken);
        var payload = JsonNode.Parse(body)?.AsObject() ?? new JsonObject();
        return await TestIndexerPayloadAsync(configuration, payload, indexer.Id, indexer.Name, indexer.Enable, indexer.Protocol, indexer.Implementation, cancellationToken);
    }

    public async Task<IndexerHealthViewModel> TestIndexerPayloadAsync(
        SetupDraft configuration,
        JsonObject payload,
        int indexerId,
        string? name,
        bool enabled,
        string? protocol,
        string? implementation,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/indexer/test")
        {
            Content = new StringContent(payload.ToJsonString())
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var responsePayload = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            var success = response.IsSuccessStatusCode && IsSuccessPayload(responsePayload);
            return new IndexerHealthViewModel
            {
                Id = indexerId,
                Name = name ?? $"Indexer {indexerId}",
                Enabled = enabled,
                Protocol = protocol ?? "-",
                Implementation = implementation ?? "-",
                Result = success ? "OK" : "FAIL",
                Error = success ? string.Empty : BuildError(response.StatusCode, responsePayload),
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new IndexerHealthViewModel
            {
                Id = indexerId,
                Name = name ?? $"Indexer {indexerId}",
                Enabled = enabled,
                Protocol = protocol ?? "-",
                Implementation = implementation ?? "-",
                Result = "FAIL",
                Error = ex.Message,
                LatencyMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static async Task<string> GetIndexerPayloadAsync(HttpClient client, int indexerId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/indexer/{indexerId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task TryAppendDefaultIndexerCategoriesAsync(
        SetupDraft configuration,
        IDictionary<int, ProwlarrCategoryOption> target,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(configuration, isMutation: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer/categories");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (JsonNode.Parse(content) is JsonArray categories)
        {
            AppendCategories(categories, 0, target);
        }
    }

    private async Task TryAppendConfiguredIndexerCategoriesAsync(
        SetupDraft configuration,
        IDictionary<int, ProwlarrCategoryOption> target,
        CancellationToken cancellationToken)
    {
        var content = await GetIndexersPayloadAsync(configuration, cancellationToken);
        if (JsonNode.Parse(content) is not JsonArray indexers)
        {
            return;
        }

        foreach (var indexer in indexers.OfType<JsonObject>())
        {
            if (indexer["categories"] is JsonArray categories)
            {
                AppendCategories(categories, 0, target);
            }

            if (indexer["capabilities"]?["categories"] is JsonArray capabilityCategories)
            {
                AppendCategories(capabilityCategories, 0, target);
            }

            if (indexer["fields"] is JsonArray fields)
            {
                AppendCategoriesFromFields(fields, target);
            }
        }
    }

    private HttpClient CreateClient(SetupDraft configuration, bool isMutation)
    {
        var client = _httpClientFactory.CreateClient(nameof(ProwlarrApiClient));
        client.BaseAddress = new Uri(configuration.ProwlarrUrl);
        client.Timeout = TimeSpan.FromSeconds(isMutation
            ? Math.Clamp(configuration.HealthCheckTimeoutSeconds * 6, 30, 300)
            : Math.Clamp(configuration.HealthCheckTimeoutSeconds, 5, 180));
        client.DefaultRequestHeaders.Clear();
        if (!string.IsNullOrWhiteSpace(configuration.ProwlarrApiKey))
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", configuration.ProwlarrApiKey);
        }

        return client;
    }

    private static bool IsSuccessPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Trim() == "{}")
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("result", out var result) && string.Equals(result.GetString(), "success", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (root.TryGetProperty("successful", out var successful) && successful.ValueKind == JsonValueKind.True)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static string BuildError(System.Net.HttpStatusCode statusCode, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return $"HTTP {(int)statusCode}";
        }

        var extracted = ExtractErrorMessage(payload);
        if (!string.IsNullOrWhiteSpace(extracted))
        {
            return $"HTTP {(int)statusCode}: {extracted}";
        }

        return $"HTTP {(int)statusCode}: {payload}";
    }

    private static void AppendCategories(JsonArray categories, int depth, IDictionary<int, ProwlarrCategoryOption> target)
    {
        foreach (var item in categories.OfType<JsonObject>())
        {
            var id = item["id"]?.GetValue<int>() ?? 0;
            var name = item["name"]?.GetValue<string>() ?? string.Empty;
            if (id > 0 && !string.IsNullOrWhiteSpace(name) && !target.ContainsKey(id))
            {
                target[id] = new ProwlarrCategoryOption
                {
                    Id = id,
                    Name = name,
                    Depth = depth
                };
            }

            if (item["subCategories"] is JsonArray subCategories)
            {
                AppendCategories(subCategories, depth + 1, target);
            }
        }
    }

    private static void AppendCategoriesFromFields(JsonArray fields, IDictionary<int, ProwlarrCategoryOption> target)
    {
        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            if (!TryParseCategoryIdFromFieldName(name, out var categoryId))
            {
                continue;
            }

            if (!target.ContainsKey(categoryId))
            {
                var label = field["label"]?.GetValue<string>()
                    ?? field["helpText"]?.GetValue<string>()
                    ?? $"Category {categoryId}";
                target[categoryId] = new ProwlarrCategoryOption
                {
                    Id = categoryId,
                    Name = label,
                    Depth = 0
                };
            }
        }
    }

    private static bool TryParseCategoryIdFromFieldName(string? fieldName, out int categoryId)
    {
        categoryId = 0;
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        const string marker = "category_";
        var index = fieldName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var suffix = fieldName[(index + marker.Length)..];
        return int.TryParse(suffix, out categoryId) && categoryId > 0;
    }

    private static string? ExtractErrorMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var messages = new List<string>();
            CollectErrorMessages(document.RootElement, messages);
            if (messages.Count > 0)
            {
                return string.Join(" | ", messages.Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }
        catch
        {
        }

        return null;
    }

    private static void CollectErrorMessages(JsonElement element, ICollection<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String
                        && IsErrorMessageProperty(property.Name))
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            messages.Add(value);
                        }
                    }
                    else
                    {
                        CollectErrorMessages(property.Value, messages);
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectErrorMessages(item, messages);
                }
                break;
        }
    }

    private static bool IsErrorMessageProperty(string propertyName)
        => string.Equals(propertyName, "message", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "description", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "errorMessage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "propertyName", StringComparison.OrdinalIgnoreCase);

    private static string T(string? language, string key)
        => UiTextCatalog.Get(language, key);
}
