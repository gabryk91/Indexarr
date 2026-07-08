using Indexarr.Web.Data;
using Indexarr.Web.Data.Entities;
using Indexarr.Web.Models;
using Indexarr.Web.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Indexarr.Web.Services;

public sealed class AppConfigurationService
{
    private readonly IndexarrDbContext _dbContext;
    private readonly IndexarrOptions _options;

    public AppConfigurationService(IndexarrDbContext dbContext, IOptions<IndexarrOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
    }

    public async Task<SetupDraft?> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AppConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return entity is null ? null : ToDraft(entity);
    }

    public async Task SaveAsync(SetupDraft draft, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AppConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (entity is null)
        {
            entity = new AppConfigurationEntity();
            _dbContext.AppConfigurations.Add(entity);
        }

        entity.Language = draft.Language;
        entity.Timezone = draft.Timezone;
        entity.ServiceMode = draft.Mode;
        entity.FailureThreshold = draft.FailureThreshold;
        entity.HealthCheckTimeoutSeconds = draft.HealthCheckTimeoutSeconds;
        entity.AutomationEnabled = draft.AutomationEnabled;
        entity.AutomationIntervalMinutes = draft.AutomationIntervalMinutes;
        entity.BackupBeforeChanges = draft.BackupBeforeChanges;
        entity.AutoDisableFailedIndexers = draft.AutoDisableFailedIndexers;
        entity.AutoAddPublicIndexers = draft.AutoAddPublicIndexers;
        entity.AutoAddProtocolFilter = draft.AutoAddProtocolFilter;
        entity.AutoAddLanguageFilter = draft.AutoAddLanguageFilter;
        entity.AutoAddCategoryFilter = draft.AutoAddCategoryFilter;
        entity.AutoAddLanguageMatchMode = draft.AutoAddLanguageMatchMode;
        entity.AutoAddAllowUnknownLanguage = draft.AutoAddAllowUnknownLanguage;
        entity.AutoAddPrivacyFilter = draft.AutoAddPrivacyFilter;
        entity.AutoAddGlobalUsername = draft.AutoAddGlobalUsername ?? string.Empty;
        entity.AutoAddGlobalPassword = draft.AutoAddGlobalPassword ?? string.Empty;
        entity.AutoAddFailureCooldownHours = draft.AutoAddFailureCooldownHours <= 0 ? 24 : draft.AutoAddFailureCooldownHours;
        entity.AutoAddDefaultQueryLimit = draft.AutoAddDefaultQueryLimit;
        entity.AutoAddDefaultGrabLimit = draft.AutoAddDefaultGrabLimit;
        entity.AutoAddDefaultLimitsUnit = draft.AutoAddDefaultLimitsUnit ?? string.Empty;
        entity.AutoAddDefaultAppMinimumSeeders = draft.AutoAddDefaultAppMinimumSeeders;
        entity.AutoAddDefaultSeedRatio = draft.AutoAddDefaultSeedRatio;
        entity.AutoAddDefaultSeedTime = draft.AutoAddDefaultSeedTime;
        entity.AutoAddDefaultPackSeedTime = draft.AutoAddDefaultPackSeedTime;
        entity.AutoAddDefaultPreferMagnetUrlMode = draft.AutoAddDefaultPreferMagnetUrlMode;
        entity.AutoAddDefaultIndexerPriority = draft.AutoAddDefaultIndexerPriority;
        entity.AutoAddDefaultDownloadClient = draft.AutoAddDefaultDownloadClient ?? string.Empty;
        entity.AutoAddDefaultFilterByUploader = draft.AutoAddDefaultFilterByUploader ?? string.Empty;
        entity.AutoAddDefaultTags = draft.AutoAddDefaultTags ?? string.Empty;
        entity.ProwlarrUrl = draft.ProwlarrUrl;
        entity.ProwlarrApiKey = draft.ProwlarrApiKey;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateServiceModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AppConfigurations.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.ServiceMode = mode;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetModeAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.AppConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return entity?.ServiceMode ?? "DryRun";
    }

    public async Task EnsureMigratedFromDraftAsync(SetupDraftStore setupDraftStore, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.AppConfigurations.AnyAsync(cancellationToken);
        if (existing)
        {
            return;
        }

        var draft = await setupDraftStore.LoadAsync(cancellationToken);
        if (draft is not null)
        {
            await SaveAsync(draft, cancellationToken);
        }
    }

    private SetupDraft ToDraft(AppConfigurationEntity entity)
        => new()
        {
            Language = entity.Language,
            Timezone = entity.Timezone,
            Mode = entity.ServiceMode,
            FailureThreshold = entity.FailureThreshold,
            HealthCheckTimeoutSeconds = entity.HealthCheckTimeoutSeconds,
            AutomationEnabled = entity.AutomationEnabled,
            AutomationIntervalMinutes = entity.AutomationIntervalMinutes <= 0 ? _options.Automation.IntervalMinutes : entity.AutomationIntervalMinutes,
            BackupBeforeChanges = entity.BackupBeforeChanges,
            AutoDisableFailedIndexers = entity.AutoDisableFailedIndexers,
            AutoAddPublicIndexers = entity.AutoAddPublicIndexers,
            AutoAddProtocolFilter = entity.AutoAddProtocolFilter,
            AutoAddLanguageFilter = entity.AutoAddLanguageFilter,
            AutoAddCategoryFilter = entity.AutoAddCategoryFilter,
            AutoAddLanguageMatchMode = entity.AutoAddLanguageMatchMode,
            AutoAddAllowUnknownLanguage = entity.AutoAddAllowUnknownLanguage,
            AutoAddPrivacyFilter = entity.AutoAddPrivacyFilter,
            AutoAddGlobalUsername = entity.AutoAddGlobalUsername,
            AutoAddGlobalPassword = entity.AutoAddGlobalPassword,
            AutoAddFailureCooldownHours = entity.AutoAddFailureCooldownHours <= 0 ? 24 : entity.AutoAddFailureCooldownHours,
            AutoAddDefaultQueryLimit = entity.AutoAddDefaultQueryLimit,
            AutoAddDefaultGrabLimit = entity.AutoAddDefaultGrabLimit,
            AutoAddDefaultLimitsUnit = entity.AutoAddDefaultLimitsUnit,
            AutoAddDefaultAppMinimumSeeders = entity.AutoAddDefaultAppMinimumSeeders,
            AutoAddDefaultSeedRatio = entity.AutoAddDefaultSeedRatio,
            AutoAddDefaultSeedTime = entity.AutoAddDefaultSeedTime,
            AutoAddDefaultPackSeedTime = entity.AutoAddDefaultPackSeedTime,
            AutoAddDefaultPreferMagnetUrlMode = entity.AutoAddDefaultPreferMagnetUrlMode,
            AutoAddDefaultIndexerPriority = entity.AutoAddDefaultIndexerPriority,
            AutoAddDefaultDownloadClient = entity.AutoAddDefaultDownloadClient,
            AutoAddDefaultFilterByUploader = entity.AutoAddDefaultFilterByUploader,
            AutoAddDefaultTags = entity.AutoAddDefaultTags,
            ProwlarrUrl = entity.ProwlarrUrl,
            ProwlarrApiKey = entity.ProwlarrApiKey
        };
}
