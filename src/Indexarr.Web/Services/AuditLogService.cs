using Indexarr.Web.Data;
using Indexarr.Web.Data.Entities;

namespace Indexarr.Web.Services;

public sealed class AuditLogService
{
    private readonly IndexarrDbContext _dbContext;

    public AuditLogService(IndexarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task WriteAsync(string action, string mode, bool succeeded, string details, int? indexerId = null, string? indexerName = null, CancellationToken cancellationToken = default)
    {
        _dbContext.AuditLogs.Add(new AuditLogEntity
        {
            IndexerId = indexerId,
            IndexerName = indexerName ?? string.Empty,
            Action = action,
            Mode = mode,
            Succeeded = succeeded,
            Details = details,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
