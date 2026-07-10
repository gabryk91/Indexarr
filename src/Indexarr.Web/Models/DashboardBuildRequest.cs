namespace Indexarr.Web.Models;

public sealed class DashboardBuildRequest
{
    public string StatusFilter { get; init; } = "all";

    public string ProtocolFilter { get; init; } = "all";

    public string AuditDateFilter { get; init; } = string.Empty;

    public string AuditActionFilter { get; init; } = "all";

    public string AuditResultFilter { get; init; } = "all";

    public int IndexerPage { get; init; } = 1;

    public int AuditPage { get; init; } = 1;
}
