namespace Indexarr.Web.Data.Entities;

public sealed class IndexerHealthCheckEntity
{
    public long Id { get; set; }

    public int IndexerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string Implementation { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;

    public long LatencyMs { get; set; }

    public DateTimeOffset CheckedAtUtc { get; set; }
}
