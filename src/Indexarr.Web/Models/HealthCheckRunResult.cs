namespace Indexarr.Web.Models;

public sealed class HealthCheckRunResult
{
    public bool Reachable { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;

    public DateTimeOffset ExecutedAtUtc { get; init; }

    public int TotalIndexers { get; init; }

    public int HealthyIndexers { get; init; }

    public int FailedIndexers { get; init; }
}
