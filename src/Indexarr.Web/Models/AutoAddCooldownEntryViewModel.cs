namespace Indexarr.Web.Models;

public sealed class AutoAddCooldownEntryViewModel
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DefinitionName { get; init; } = string.Empty;

    public int FailureCount { get; init; }

    public DateTimeOffset NextRetryAtUtc { get; init; }
}
