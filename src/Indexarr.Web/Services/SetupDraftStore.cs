using System.Text.Json;
using Indexarr.Web.Options;
using Indexarr.Web.Models;
using Microsoft.Extensions.Options;

namespace Indexarr.Web.Services;

public sealed class SetupDraftStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ILogger<SetupDraftStore> _logger;
    private readonly IndexarrOptions _options;
    private readonly StoragePathResolver _storagePathResolver;

    public SetupDraftStore(
        StoragePathResolver storagePathResolver,
        IOptions<IndexarrOptions> options,
        ILogger<SetupDraftStore> logger)
    {
        _storagePathResolver = storagePathResolver;
        _options = options.Value;
        _logger = logger;
    }

    public string DraftPath => _storagePathResolver.ResolveFilePath(_options.ConfigPath, "setup-draft.json");

    public async Task<SetupDraft?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(DraftPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(DraftPath);
        return await JsonSerializer.DeserializeAsync<SetupDraft>(stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(SetupDraft draft, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DraftPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(DraftPath);
        await JsonSerializer.SerializeAsync(stream, draft, JsonOptions, cancellationToken);
        _logger.LogInformation("Setup draft saved to {Path}", DraftPath);
    }
}
