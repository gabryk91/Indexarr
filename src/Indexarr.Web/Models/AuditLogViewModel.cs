namespace Indexarr.Web.Models;

public sealed class AuditLogViewModel
{
    public long Id { get; init; }

    public string Action { get; init; } = string.Empty;

    public string Mode { get; init; } = string.Empty;

    public string IndexerName { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public string Details { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}
