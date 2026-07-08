using System.Text;
using Indexarr.Web.Options;
using Microsoft.Extensions.Options;

namespace Indexarr.Web.Services;

public sealed class IndexerBackupService
{
    private readonly StoragePathResolver _pathResolver;
    private readonly IOptions<IndexarrOptions> _options;

    public IndexerBackupService(StoragePathResolver pathResolver, IOptions<IndexarrOptions> options)
    {
        _pathResolver = pathResolver;
        _options = options;
    }

    public async Task<string> SaveAsync(string reason, string payload, CancellationToken cancellationToken = default)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var safeReason = string.Concat(reason.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
        var filePath = _pathResolver.ResolveFilePath(_options.Value.BackupPath, $"indexers-{stamp}-{safeReason}.json");
        await File.WriteAllTextAsync(filePath, payload, Encoding.UTF8, cancellationToken);
        return filePath;
    }
}
