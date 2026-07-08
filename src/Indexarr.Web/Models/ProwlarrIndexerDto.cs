namespace Indexarr.Web.Models;

public sealed class ProwlarrIndexerDto
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public bool Enable { get; set; }

    public string? Protocol { get; set; }

    public string? Implementation { get; set; }
}
