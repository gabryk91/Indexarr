namespace Indexarr.Web.Models;

public sealed class ProwlarrIndexerUpdateResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string ResponseJson { get; init; } = string.Empty;
}
