namespace Indexarr.Web.Models;

public sealed class AutoAddRunResult
{
    public bool Reachable { get; set; }

    public bool DryRun { get; set; }

    public int CandidateCount { get; set; }

    public int AddedCount { get; set; }

    public int FailedCount { get; set; }

    public string Message { get; set; } = string.Empty;
}
