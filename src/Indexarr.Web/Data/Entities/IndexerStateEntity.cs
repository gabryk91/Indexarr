namespace Indexarr.Web.Data.Entities;

public sealed class IndexerStateEntity
{
    public int IndexerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string Implementation { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public bool AutoDisabledByHealthCheck { get; set; }

    public string LastResult { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public long LastLatencyMs { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? LastCheckedAtUtc { get; set; }

    public DateTimeOffset? LastActionAtUtc { get; set; }

    public bool IsBlocked { get; set; }
}
