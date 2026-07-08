namespace Indexarr.Web.Models;

public sealed class ProwlarrDownloadClientOption
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enable { get; set; }

    public string Protocol { get; set; } = string.Empty;

    public bool SupportsCategories { get; set; }

    public string Implementation { get; set; } = string.Empty;

    public string ImplementationName { get; set; } = string.Empty;

    public IReadOnlyList<ProwlarrDownloadClientCategoryOption> Categories { get; set; } = [];

    public IReadOnlyList<int> Tags { get; set; } = [];
}
