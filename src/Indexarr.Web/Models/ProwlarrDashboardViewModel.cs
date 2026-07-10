namespace Indexarr.Web.Models;

public sealed class ProwlarrDashboardViewModel
{
    public bool Reachable { get; init; }

    public string InstanceName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public int TotalIndexers { get; init; }

    public int EnabledIndexers { get; init; }

    public int HealthyIndexers { get; init; }

    public int FailedIndexers { get; init; }

    public int BlockedIndexers { get; init; }

    public DateTimeOffset? LastRunAtUtc { get; init; }

    public IReadOnlyList<string> AvailableProtocols { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> AvailableAuditActions { get; init; } = Array.Empty<string>();

    public DashboardFilterModel Filters { get; init; } = new();

    public IReadOnlyList<AuditLogViewModel> AuditLogs { get; init; } = Array.Empty<AuditLogViewModel>();

    public IReadOnlyList<IndexerHealthViewModel> Indexers { get; init; } = Array.Empty<IndexerHealthViewModel>();

    public int IndexerPage { get; init; } = 1;

    public int IndexerPageSize { get; init; } = 10;

    public int IndexerTotalCount { get; init; }

    public int IndexerTotalPages { get; init; }

    public int AuditPage { get; init; } = 1;

    public int AuditPageSize { get; init; } = 15;

    public int AuditTotalCount { get; init; }

    public int AuditTotalPages { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;
}
