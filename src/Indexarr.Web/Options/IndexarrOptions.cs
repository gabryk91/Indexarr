namespace Indexarr.Web.Options;

public sealed class IndexarrOptions
{
    public const string SectionName = "Indexarr";

    public string ProductName { get; set; } = "Indexarr";

    public string Mode { get; set; } = "DryRun";

    public string ConfigPath { get; set; } = "/config";

    public string BackupPath { get; set; } = "/backups";

    public string LogsPath { get; set; } = "/logs";

    public ProwlarrOptions Prowlarr { get; set; } = new();

    public AutomationOptions Automation { get; set; } = new();
}

public sealed class ProwlarrOptions
{
    public string Url { get; set; } = "http://127.0.0.1:9696";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class AutomationOptions
{
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 60;
}
