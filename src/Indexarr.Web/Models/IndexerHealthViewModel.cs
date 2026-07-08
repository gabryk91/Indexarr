namespace Indexarr.Web.Models;

public sealed class IndexerHealthViewModel
{
    public int Id { get; init; }

    public int? BlockedIndexerId { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string Implementation { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public long LatencyMs { get; init; }

    public DateTimeOffset? LastCheckedAtUtc { get; init; }

    public int ConsecutiveFailures { get; init; }

    public string Trend { get; init; } = "Flat";

    public bool IsBlocked { get; init; }
}
