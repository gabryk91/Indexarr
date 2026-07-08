namespace Indexarr.Web.Data.Entities;

public sealed class AuditLogEntity
{
    public long Id { get; set; }

    public int? IndexerId { get; set; }

    public string IndexerName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public string Details { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
