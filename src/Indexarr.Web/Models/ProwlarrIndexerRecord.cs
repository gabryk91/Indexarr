namespace Indexarr.Web.Models;

public sealed class ProwlarrIndexerRecord
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public string Protocol { get; init; } = string.Empty;

    public string Implementation { get; init; } = string.Empty;

    public string PayloadJson { get; init; } = string.Empty;
}
