namespace Indexarr.Web.Data.Entities;

public sealed class BlockedIndexerEntity
{
    public int Id { get; set; }

    public int OriginalIndexerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DefinitionName { get; set; } = string.Empty;

    public string Protocol { get; set; } = string.Empty;

    public string Implementation { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset BlockedAtUtc { get; set; }
}
