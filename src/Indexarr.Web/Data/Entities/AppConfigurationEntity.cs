namespace Indexarr.Web.Data.Entities;

public sealed class AppConfigurationEntity
{
    public int Id { get; set; } = 1;

    public string Language { get; set; } = "it";

    public string Timezone { get; set; } = "Europe/Rome";

    public string ServiceMode { get; set; } = "DryRun";

    public int FailureThreshold { get; set; } = 4;

    public int HealthCheckTimeoutSeconds { get; set; } = 30;

    public bool AutomationEnabled { get; set; } = true;

    public int AutomationIntervalMinutes { get; set; } = 60;

    public bool BackupBeforeChanges { get; set; } = true;

    public bool AutoDisableFailedIndexers { get; set; }

    public bool AutoAddPublicIndexers { get; set; }

    public string AutoAddProtocolFilter { get; set; } = "torrent";

    public string AutoAddLanguageFilter { get; set; } = "it,en";

    public string AutoAddCategoryFilter { get; set; } = string.Empty;

    public string AutoAddLanguageMatchMode { get; set; } = "any";

    public bool AutoAddAllowUnknownLanguage { get; set; }

    public string AutoAddPrivacyFilter { get; set; } = "public";

    public string AutoAddGlobalUsername { get; set; } = string.Empty;

    public string AutoAddGlobalPassword { get; set; } = string.Empty;

    public int AutoAddFailureCooldownHours { get; set; } = 24;

    public int? AutoAddDefaultQueryLimit { get; set; }

    public int? AutoAddDefaultGrabLimit { get; set; }

    public string AutoAddDefaultLimitsUnit { get; set; } = string.Empty;

    public int? AutoAddDefaultAppMinimumSeeders { get; set; }

    public decimal? AutoAddDefaultSeedRatio { get; set; }

    public int? AutoAddDefaultSeedTime { get; set; }

    public int? AutoAddDefaultPackSeedTime { get; set; }

    public string AutoAddDefaultPreferMagnetUrlMode { get; set; } = "inherit";

    public int? AutoAddDefaultIndexerPriority { get; set; }

    public string AutoAddDefaultDownloadClient { get; set; } = string.Empty;

    public string AutoAddDefaultFilterByUploader { get; set; } = string.Empty;

    public string AutoAddDefaultTags { get; set; } = string.Empty;

    public string ProwlarrUrl { get; set; } = "http://127.0.0.1:9696";

    public string ProwlarrApiKey { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
