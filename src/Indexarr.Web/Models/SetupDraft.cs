using System.ComponentModel.DataAnnotations;

namespace Indexarr.Web.Models;

public sealed class SetupDraft
{
    [Required]
    [RegularExpression("it|en", ErrorMessage = "Language must be it or en.")]
    public string Language { get; set; } = "it";

    [Required]
    [Display(Name = "Prowlarr URL")]
    [Url]
    public string ProwlarrUrl { get; set; } = "http://127.0.0.1:9696";

    [Display(Name = "Prowlarr API Key")]
    public string ProwlarrApiKey { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Mode")]
    public string Mode { get; set; } = "DryRun";

    [Range(1, 20)]
    [Display(Name = "Failure threshold")]
    public int FailureThreshold { get; set; } = 4;

    [Range(5, 120)]
    [Display(Name = "Health check timeout seconds")]
    public int HealthCheckTimeoutSeconds { get; set; } = 30;

    [Display(Name = "Enable scheduled automation")]
    public bool AutomationEnabled { get; set; } = true;

    [Range(1, 1440)]
    [Display(Name = "Automation interval minutes")]
    public int AutomationIntervalMinutes { get; set; } = 60;

    [Display(Name = "Auto backup before changes")]
    public bool BackupBeforeChanges { get; set; } = true;

    [Display(Name = "Auto disable after repeated failures")]
    public bool AutoDisableFailedIndexers { get; set; } = false;

    [Display(Name = "Add new indexers automatically")]
    public bool AutoAddPublicIndexers { get; set; } = false;

    [Display(Name = "Auto-add protocol filter")]
    public string AutoAddProtocolFilter { get; set; } = "torrent";

    [Display(Name = "Auto-add language filter")]
    public string AutoAddLanguageFilter { get; set; } = "en-GB,en-US";

    [Display(Name = "Auto-add category filter")]
    public string AutoAddCategoryFilter { get; set; } = string.Empty;

    [Display(Name = "Auto-add language match mode")]
    public string AutoAddLanguageMatchMode { get; set; } = "any";

    [Display(Name = "Allow indexers without declared language")]
    public bool AutoAddAllowUnknownLanguage { get; set; }

    [Display(Name = "Auto-add privacy filter")]
    public string AutoAddPrivacyFilter { get; set; } = "public";

    [Display(Name = "Global username for semi-private indexers")]
    public string? AutoAddGlobalUsername { get; set; }

    [Display(Name = "Global password for semi-private indexers")]
    public string? AutoAddGlobalPassword { get; set; }

    [Range(1, 168)]
    [Display(Name = "Auto-add failure cooldown hours")]
    public int AutoAddFailureCooldownHours { get; set; } = 24;

    [Display(Name = "Default query limit for new indexers")]
    public int? AutoAddDefaultQueryLimit { get; set; }

    [Display(Name = "Default grab limit for new indexers")]
    public int? AutoAddDefaultGrabLimit { get; set; }

    [Display(Name = "Default limits unit for new indexers")]
    public string? AutoAddDefaultLimitsUnit { get; set; }

    [Display(Name = "Default apps minimum seeders for new indexers")]
    public int? AutoAddDefaultAppMinimumSeeders { get; set; }

    [Display(Name = "Default seed ratio for new indexers")]
    public decimal? AutoAddDefaultSeedRatio { get; set; }

    [Display(Name = "Default seed time for new indexers")]
    public int? AutoAddDefaultSeedTime { get; set; }

    [Display(Name = "Default pack seed time for new indexers")]
    public int? AutoAddDefaultPackSeedTime { get; set; }

    [Display(Name = "Default prefer magnet URL mode for new indexers")]
    public string AutoAddDefaultPreferMagnetUrlMode { get; set; } = "inherit";

    [Display(Name = "Default indexer priority for new indexers")]
    public int? AutoAddDefaultIndexerPriority { get; set; }

    [Display(Name = "Default download client for new indexers")]
    public string? AutoAddDefaultDownloadClient { get; set; }

    [Display(Name = "Default uploader filter for new indexers")]
    public string? AutoAddDefaultFilterByUploader { get; set; }

    [Display(Name = "Default tags for new indexers")]
    public string? AutoAddDefaultTags { get; set; }

    [Required]
    [Display(Name = "Timezone")]
    public string Timezone { get; set; } = "Europe/Rome";
}
