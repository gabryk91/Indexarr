namespace Indexarr.Web.Models;

public sealed class DashboardFilterModel
{
    public string Status { get; init; } = "all";

    public string Protocol { get; init; } = "all";

    public string AuditDate { get; init; } = string.Empty;

    public string AuditAction { get; init; } = "all";

    public string AuditResult { get; init; } = "all";
}
