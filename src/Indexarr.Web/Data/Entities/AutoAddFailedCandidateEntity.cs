namespace Indexarr.Web.Data.Entities;

public sealed class AutoAddFailedCandidateEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DefinitionName { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public int FailureCount { get; set; }

    public DateTimeOffset LastAttemptAtUtc { get; set; }

    public DateTimeOffset NextRetryAtUtc { get; set; }
}
