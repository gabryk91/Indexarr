namespace Indexarr.Web.Models;

public sealed class ProwlarrAppProfileOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool EnableRss { get; set; }

    public bool EnableAutomaticSearch { get; set; }

    public bool EnableInteractiveSearch { get; set; }

    public int MinimumSeeders { get; set; }
}
