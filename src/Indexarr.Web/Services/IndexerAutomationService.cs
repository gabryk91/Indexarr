using System.Text.Json;
using System.Text.Json.Nodes;
using Indexarr.Web.Data;
using Indexarr.Web.Data.Entities;
using Indexarr.Web.Models;
using Indexarr.Web.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Indexarr.Web.Services;

public sealed class IndexerAutomationService
{
    private readonly AppConfigurationService _configurationService;
    private readonly ProwlarrApiClient _apiClient;
    private readonly IndexarrDbContext _dbContext;
    private readonly AuditLogService _auditLogService;
    private readonly IndexerBackupService _backupService;
    private readonly NotificationDispatchService _notificationDispatchService;
    private readonly ILogger<IndexerAutomationService> _logger;

    public IndexerAutomationService(
        AppConfigurationService configurationService,
        ProwlarrApiClient apiClient,
        IndexarrDbContext dbContext,
        AuditLogService auditLogService,
        IndexerBackupService backupService,
        NotificationDispatchService notificationDispatchService,
        ILogger<IndexerAutomationService> logger)
    {
        _configurationService = configurationService;
        _apiClient = apiClient;
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _backupService = backupService;
        _notificationDispatchService = notificationDispatchService;
        _logger = logger;
    }

    public async Task<HealthCheckRunResult> RunHealthChecksAsync(string trigger, CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationService.GetAsync(cancellationToken);
        if (configuration is null)
        {
            return new HealthCheckRunResult
            {
                Reachable = false,
                ErrorMessage = T(null, "ConfigurationNotFound"),
                ExecutedAtUtc = DateTimeOffset.UtcNow
            };
        }

        try
        {
            var indexers = await _apiClient.GetIndexersAsync(configuration, cancellationToken);
            var autoDisabledIndexerIds = await _dbContext.IndexerStates
                .AsNoTracking()
                .Where(x => x.AutoDisabledByHealthCheck)
                .Select(x => x.IndexerId)
                .ToHashSetAsync(cancellationToken);
            var tasks = indexers.Select(indexer => _apiClient.TestIndexerAsync(
                configuration,
                indexer,
                autoDisabledIndexerIds.Contains(indexer.Id),
                cancellationToken));
            var results = await Task.WhenAll(tasks);
            var executedAtUtc = DateTimeOffset.UtcNow;

            foreach (var result in results)
            {
                var effectiveResult = await TryRecoverBaseUrlAsync(configuration, result, trigger, cancellationToken);
                await PersistResultAsync(effectiveResult, executedAtUtc, cancellationToken);
                await ApplyAutomaticRecoveryAsync(configuration, effectiveResult, trigger, cancellationToken);
                await ApplyAutomaticPoliciesAsync(configuration, effectiveResult, trigger, cancellationToken);
            }

            await _auditLogService.WriteAsync(
                action: "HealthCheckRun",
                mode: configuration.Mode,
                succeeded: true,
                details: $"{trigger}: {results.Count(x => x.Result == "OK")} OK, {results.Count(x => x.Result == "FAIL")} FAIL.",
                cancellationToken: cancellationToken);

            return new HealthCheckRunResult
            {
                Reachable = true,
                ExecutedAtUtc = executedAtUtc,
                TotalIndexers = results.Length,
                HealthyIndexers = results.Count(x => x.Result == "OK"),
                FailedIndexers = results.Count(x => x.Result == "FAIL")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check run failed.");
            await _auditLogService.WriteAsync(
                action: "HealthCheckRun",
                mode: configuration.Mode,
                succeeded: false,
                details: $"{trigger}: {ex.Message}",
                cancellationToken: cancellationToken);

            await _notificationDispatchService.NotifyAsync(
                NotificationEvent.ProwlarrUnreachable,
                string.Format(T(configuration.Language, "NotifyMessageProwlarrUnreachable"), ex.Message),
                cancellationToken);

            return new HealthCheckRunResult
            {
                Reachable = false,
                ErrorMessage = ex.Message,
                ExecutedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<AutoAddRunResult> RunAutoAddAsync(string trigger, CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationService.GetAsync(cancellationToken);
        if (configuration is null)
        {
            return new AutoAddRunResult
            {
                Reachable = false,
                Message = T(null, "ConfigurationNotFound")
            };
        }

        try
        {
            var indexers = await _apiClient.GetIndexersAsync(configuration, cancellationToken);
            return await ApplyAutoAddPoliciesAsync(configuration, indexers, trigger, cancellationToken);
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync(
                action: string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase) ? "AutoAddPolicy" : "AutoAddPolicyDryRun",
                mode: configuration.Mode,
                succeeded: false,
                details: $"{trigger}: {ex.Message}",
                cancellationToken: cancellationToken);

            return new AutoAddRunResult
            {
                Reachable = false,
                DryRun = !string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase),
                Message = ex.Message
            };
        }
    }

    public async Task<ProwlarrIndexerUpdateResult> SetIndexerEnabledAsync(int indexerId, bool enabled, CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationService.GetAsync(cancellationToken);
        if (configuration is null)
        {
            return new ProwlarrIndexerUpdateResult { Success = false, Message = T(null, "ConfigurationNotFound") };
        }

        try
        {
            if (configuration.BackupBeforeChanges)
            {
                var raw = await _apiClient.GetIndexersPayloadAsync(configuration, cancellationToken);
                var backupPath = await _backupService.SaveAsync(enabled ? "enable" : "disable", raw, cancellationToken);
                await _auditLogService.WriteAsync("Backup", configuration.Mode, true, backupPath, indexerId, cancellationToken: cancellationToken);
            }

            var record = await _apiClient.GetIndexerRecordAsync(configuration, indexerId, cancellationToken);
            var update = await _apiClient.SetIndexerEnabledAsync(configuration, indexerId, enabled, cancellationToken);

            await _auditLogService.WriteAsync(
                action: enabled ? "EnableIndexer" : "DisableIndexer",
                mode: configuration.Mode,
                succeeded: update.Success,
                details: update.Message,
                indexerId: indexerId,
                indexerName: record.Name,
                cancellationToken: cancellationToken);

            if (update.Success)
            {
                var state = await _dbContext.IndexerStates.SingleOrDefaultAsync(x => x.IndexerId == indexerId, cancellationToken);
                if (state is not null)
                {
                    state.Enabled = enabled;
                    state.AutoDisabledByHealthCheck = false;
                    state.IsBlocked = false;
                    state.LastActionAtUtc = DateTimeOffset.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            return update;
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync(
                action: enabled ? "EnableIndexer" : "DisableIndexer",
                mode: configuration.Mode,
                succeeded: false,
                details: ex.Message,
                indexerId: indexerId,
                cancellationToken: cancellationToken);

            return new ProwlarrIndexerUpdateResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<ProwlarrIndexerUpdateResult> BlockIndexerAsync(int indexerId, CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationService.GetAsync(cancellationToken);
        if (configuration is null)
        {
            return new ProwlarrIndexerUpdateResult { Success = false, Message = T(null, "ConfigurationNotFound") };
        }

        try
        {
            if (configuration.BackupBeforeChanges)
            {
                var raw = await _apiClient.GetIndexersPayloadAsync(configuration, cancellationToken);
                var backupPath = await _backupService.SaveAsync("block", raw, cancellationToken);
                await _auditLogService.WriteAsync("Backup", configuration.Mode, true, backupPath, indexerId, cancellationToken: cancellationToken);
            }

            var record = await _apiClient.GetIndexerRecordAsync(configuration, indexerId, cancellationToken);
            var payload = JsonNode.Parse(record.PayloadJson)?.AsObject();
            if (payload is null)
            {
                return new ProwlarrIndexerUpdateResult { Success = false, Message = T(configuration.Language, "InvalidIndexerPayload") };
            }

            var delete = await _apiClient.DeleteIndexerAsync(configuration, indexerId, cancellationToken);
            if (!delete.Success)
            {
                await _auditLogService.WriteAsync("BlockIndexer", configuration.Mode, false, delete.Message, indexerId, record.Name, cancellationToken);
                return delete;
            }

            var definitionName = payload["definitionName"]?.GetValue<string>() ?? string.Empty;
            var blocked = await _dbContext.BlockedIndexers
                .SingleOrDefaultAsync(
                    x => x.OriginalIndexerId == indexerId
                        || (!string.IsNullOrWhiteSpace(definitionName) && x.DefinitionName == definitionName)
                        || x.Name == record.Name,
                    cancellationToken);

            if (blocked is null)
            {
                blocked = new BlockedIndexerEntity();
                _dbContext.BlockedIndexers.Add(blocked);
            }

            blocked.OriginalIndexerId = indexerId;
            blocked.Name = record.Name;
            blocked.DefinitionName = definitionName;
            blocked.Protocol = record.Protocol;
            blocked.Implementation = record.Implementation;
            blocked.PayloadJson = record.PayloadJson;
            blocked.BlockedAtUtc = DateTimeOffset.UtcNow;

            var state = await _dbContext.IndexerStates.SingleOrDefaultAsync(x => x.IndexerId == indexerId, cancellationToken);
            if (state is not null)
            {
                state.Enabled = false;
                state.IsBlocked = true;
                state.LastActionAtUtc = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditLogService.WriteAsync("BlockIndexer", configuration.Mode, true, delete.Message, indexerId, record.Name, cancellationToken);
            return new ProwlarrIndexerUpdateResult
            {
                Success = true,
                Message = T(configuration.Language, "IndexerBlocked"),
                ResponseJson = delete.ResponseJson
            };
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync("BlockIndexer", configuration.Mode, false, ex.Message, indexerId, cancellationToken: cancellationToken);
            return new ProwlarrIndexerUpdateResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<ProwlarrIndexerUpdateResult> UnblockIndexerAsync(int blockedIndexerId, CancellationToken cancellationToken = default)
    {
        var configuration = await _configurationService.GetAsync(cancellationToken);
        if (configuration is null)
        {
            return new ProwlarrIndexerUpdateResult { Success = false, Message = T(null, "ConfigurationNotFound") };
        }

        var blocked = await _dbContext.BlockedIndexers.SingleOrDefaultAsync(x => x.Id == blockedIndexerId, cancellationToken);
        if (blocked is null)
        {
            return new ProwlarrIndexerUpdateResult { Success = false, Message = T(configuration.Language, "BlockedIndexerNotFound") };
        }

        try
        {
            var payload = JsonNode.Parse(blocked.PayloadJson)?.AsObject();
            if (payload is null)
            {
                return new ProwlarrIndexerUpdateResult { Success = false, Message = T(configuration.Language, "InvalidBlockedIndexerPayload") };
            }

            var create = await _apiClient.CreateIndexerAsync(configuration, payload, cancellationToken);
            if (!create.Success)
            {
                await _auditLogService.WriteAsync("UnblockIndexer", configuration.Mode, false, create.Message, blocked.OriginalIndexerId, blocked.Name, cancellationToken);
                return create;
            }

            _dbContext.BlockedIndexers.Remove(blocked);

            var state = await _dbContext.IndexerStates.SingleOrDefaultAsync(x => x.IndexerId == blocked.OriginalIndexerId, cancellationToken);
            if (state is not null)
            {
                state.IsBlocked = false;
                state.LastActionAtUtc = DateTimeOffset.UtcNow;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditLogService.WriteAsync("UnblockIndexer", configuration.Mode, true, create.Message, blocked.OriginalIndexerId, blocked.Name, cancellationToken);
            return new ProwlarrIndexerUpdateResult
            {
                Success = true,
                Message = T(configuration.Language, "IndexerUnblockedRecreated"),
                ResponseJson = create.ResponseJson
            };
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync("UnblockIndexer", configuration.Mode, false, ex.Message, blocked.OriginalIndexerId, blocked.Name, cancellationToken);
            return new ProwlarrIndexerUpdateResult { Success = false, Message = ex.Message };
        }
    }

    private async Task PersistResultAsync(IndexerHealthViewModel result, DateTimeOffset checkedAtUtc, CancellationToken cancellationToken)
    {
        _dbContext.IndexerHealthChecks.Add(new IndexerHealthCheckEntity
        {
            IndexerId = result.Id,
            Name = result.Name,
            Protocol = result.Protocol,
            Implementation = result.Implementation,
            Result = result.Result,
            Error = result.Error,
            LatencyMs = result.LatencyMs,
            CheckedAtUtc = checkedAtUtc
        });

        var state = await _dbContext.IndexerStates.SingleOrDefaultAsync(x => x.IndexerId == result.Id, cancellationToken);
        if (state is null)
        {
            state = new IndexerStateEntity { IndexerId = result.Id };
            _dbContext.IndexerStates.Add(state);
        }

        state.Name = result.Name;
        state.Protocol = result.Protocol;
        state.Implementation = result.Implementation;
        state.Enabled = result.Enabled;
        state.IsBlocked = false;
        state.LastResult = result.Result;
        state.LastError = result.Error;
        state.LastLatencyMs = result.LatencyMs;
        state.LastCheckedAtUtc = checkedAtUtc;
        state.ConsecutiveFailures = result.Result == "FAIL" ? state.ConsecutiveFailures + 1 : 0;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAutomaticRecoveryAsync(SetupDraft configuration, IndexerHealthViewModel result, string trigger, CancellationToken cancellationToken)
    {
        if (result.Enabled || !string.Equals(result.Result, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var state = await _dbContext.IndexerStates.SingleAsync(x => x.IndexerId == result.Id, cancellationToken);
        if (!state.AutoDisabledByHealthCheck)
        {
            return;
        }

        if (string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase))
        {
            var update = await SetIndexerEnabledAsync(result.Id, enabled: true, cancellationToken);
            await _auditLogService.WriteAsync(
                action: "AutoEnablePolicy",
                mode: configuration.Mode,
                succeeded: update.Success,
                details: $"{trigger}: {update.Message}",
                indexerId: result.Id,
                indexerName: result.Name,
                cancellationToken: cancellationToken);
            return;
        }

        await _auditLogService.WriteAsync(
            action: "AutoEnablePolicyDryRun",
            mode: configuration.Mode,
            succeeded: true,
            details: $"{trigger}: planned enable after a successful health check.",
            indexerId: result.Id,
            indexerName: result.Name,
            cancellationToken: cancellationToken);
    }

    private async Task ApplyAutomaticPoliciesAsync(SetupDraft configuration, IndexerHealthViewModel result, string trigger, CancellationToken cancellationToken)
    {
        if (!configuration.AutoDisableFailedIndexers || !result.Enabled || result.Result != "FAIL")
        {
            return;
        }

        var state = await _dbContext.IndexerStates.AsNoTracking().SingleAsync(x => x.IndexerId == result.Id, cancellationToken);
        if (state.ConsecutiveFailures < configuration.FailureThreshold)
        {
            return;
        }

        if (string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase))
        {
            var update = await SetIndexerEnabledAsync(result.Id, enabled: false, cancellationToken);
            await _auditLogService.WriteAsync(
                action: "AutoDisablePolicy",
                mode: configuration.Mode,
                succeeded: update.Success,
                details: $"{trigger}: {update.Message}",
                indexerId: result.Id,
                indexerName: result.Name,
                cancellationToken: cancellationToken);

            if (update.Success)
            {
                var updatedState = await _dbContext.IndexerStates.SingleOrDefaultAsync(x => x.IndexerId == result.Id, cancellationToken);
                if (updatedState is not null)
                {
                    updatedState.AutoDisabledByHealthCheck = true;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                await _notificationDispatchService.NotifyAsync(
                    NotificationEvent.IndexerAutoDisabled,
                    string.Format(T(configuration.Language, "NotifyMessageIndexerAutoDisabled"), result.Name, state.ConsecutiveFailures),
                    cancellationToken);
            }

            return;
        }

        await _auditLogService.WriteAsync(
            action: "AutoDisablePolicyDryRun",
            mode: configuration.Mode,
            succeeded: true,
            details: $"{trigger}: planned disable after {state.ConsecutiveFailures} consecutive failures.",
            indexerId: result.Id,
            indexerName: result.Name,
            cancellationToken: cancellationToken);
    }

    private async Task<IndexerHealthViewModel> TryRecoverBaseUrlAsync(SetupDraft configuration, IndexerHealthViewModel result, string trigger, CancellationToken cancellationToken)
    {
        if (!string.Equals(result.Result, "FAIL", StringComparison.OrdinalIgnoreCase) || !result.Enabled)
        {
            return result;
        }

        ProwlarrIndexerRecord record;
        try
        {
            record = await _apiClient.GetIndexerRecordAsync(configuration, result.Id, cancellationToken);
        }
        catch
        {
            return result;
        }

        var payload = JsonNode.Parse(record.PayloadJson)?.AsObject();
        if (payload is null || !TryGetBaseUrlCandidates(payload, out var currentBaseUrl, out var candidates))
        {
            return result;
        }

        IndexerHealthViewModel? bestResult = null;
        string? bestBaseUrl = null;

        foreach (var candidateBaseUrl in candidates)
        {
            var candidatePayload = JsonNode.Parse(record.PayloadJson)?.AsObject();
            if (candidatePayload is null)
            {
                continue;
            }

            SetBaseUrl(candidatePayload, candidateBaseUrl);
            var candidateResult = await _apiClient.TestIndexerPayloadAsync(
                configuration,
                candidatePayload,
                result.Id,
                result.Name,
                result.Enabled,
                result.Protocol,
                result.Implementation,
                cancellationToken);

            if (!string.Equals(candidateResult.Result, "OK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (bestResult is null || candidateResult.LatencyMs < bestResult.LatencyMs)
            {
                bestResult = candidateResult;
                bestBaseUrl = candidateBaseUrl;
            }
        }

        if (bestResult is null || string.IsNullOrWhiteSpace(bestBaseUrl))
        {
            return result;
        }

        if (string.Equals(bestBaseUrl, currentBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            await _auditLogService.WriteAsync(
                action: "BaseUrlRecovery",
                mode: configuration.Mode,
                succeeded: true,
                details: $"{trigger}: recovered connectivity for {result.Name} using current base URL {bestBaseUrl} ({bestResult.LatencyMs} ms).",
                indexerId: result.Id,
                indexerName: result.Name,
                cancellationToken: cancellationToken);
            return bestResult;
        }

        var updatePayload = JsonNode.Parse(record.PayloadJson)?.AsObject();
        if (updatePayload is null)
        {
            return result;
        }

        SetBaseUrl(updatePayload, bestBaseUrl);
        var update = await _apiClient.UpdateIndexerPayloadAsync(configuration, result.Id, updatePayload, cancellationToken);
        await _auditLogService.WriteAsync(
            action: "BaseUrlRecovery",
            mode: configuration.Mode,
            succeeded: update.Success,
            details: update.Success
                ? $"{trigger}: switched base URL for {result.Name} from {currentBaseUrl} to {bestBaseUrl} ({bestResult.LatencyMs} ms)."
                : $"{trigger}: best base URL {bestBaseUrl} found for {result.Name}, but update failed: {update.Message}",
            indexerId: result.Id,
            indexerName: result.Name,
            cancellationToken: cancellationToken);

        return update.Success ? bestResult : result;
    }

    private async Task<AutoAddRunResult> ApplyAutoAddPoliciesAsync(SetupDraft configuration, IReadOnlyList<ProwlarrIndexerDto> existingIndexers, string trigger, CancellationToken cancellationToken)
    {
        if (!configuration.AutoAddPublicIndexers)
        {
            return new AutoAddRunResult
            {
                Reachable = true,
                DryRun = !string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase),
                Message = "Auto-add disabled."
            };
        }

        try
        {
            var schemas = await _apiClient.GetIndexerSchemasAsync(configuration, cancellationToken);
            var allSchemas = schemas.OfType<JsonObject>().ToList();
            var appProfiles = await _apiClient.GetAppProfilesAsync(configuration, cancellationToken);
            var blockedIndexers = await _dbContext.BlockedIndexers
                .AsNoTracking()
                .Select(x => new { x.Name, x.DefinitionName })
                .ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var recentlyFailedIndexers = (await _dbContext.AutoAddFailedCandidates
                .AsNoTracking()
                .Select(x => new { x.Name, x.DefinitionName, x.NextRetryAtUtc })
                .ToListAsync(cancellationToken))
                .Where(x => x.NextRetryAtUtc > now)
                .Select(x => new { x.Name, x.DefinitionName })
                .ToList();
            IReadOnlyList<ProwlarrTagOption> availableTags;
            try
            {
                availableTags = await _apiClient.GetTagsAsync(configuration, cancellationToken);
            }
            catch
            {
                availableTags = [];
            }
            var defaultAppProfileId = appProfiles
                .Select(x => x.Id)
                .FirstOrDefault(x => x > 0);
            var candidates = SelectAutoAddCandidates(allSchemas, existingIndexers, blockedIndexers, recentlyFailedIndexers, configuration)
                .ToList();

            if (candidates.Count == 0)
            {
                var sample = string.Join(", ", allSchemas
                    .Select(x => x["name"]?.GetValue<string>() ?? x["implementation"]?.GetValue<string>() ?? "unknown")
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5));

                await _auditLogService.WriteAsync(
                    action: string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase) ? "AutoAddPolicy" : "AutoAddPolicyDryRun",
                    mode: configuration.Mode,
                    succeeded: true,
                    details: $"{trigger}: no matching indexer candidates for protocol={configuration.AutoAddProtocolFilter}, language={configuration.AutoAddLanguageFilter}, privacy={configuration.AutoAddPrivacyFilter}. Available sample: {sample}",
                    cancellationToken: cancellationToken);
                return new AutoAddRunResult
                {
                    Reachable = true,
                    DryRun = !string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase),
                    Message = "No matching candidates found."
                };
            }

            candidates = candidates
                .Where(schema =>
                {
                    var name = schema["name"]?.GetValue<string>() ?? schema["implementation"]?.GetValue<string>() ?? string.Empty;
                    return !existingIndexers.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            if (candidates.Count == 0)
            {
                await _auditLogService.WriteAsync(
                    action: string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase) ? "AutoAddPolicy" : "AutoAddPolicyDryRun",
                    mode: configuration.Mode,
                    succeeded: true,
                    details: $"{trigger}: matching candidates already present in Prowlarr for protocol={configuration.AutoAddProtocolFilter}, language={configuration.AutoAddLanguageFilter}, privacy={configuration.AutoAddPrivacyFilter}.",
                    cancellationToken: cancellationToken);
                return new AutoAddRunResult
                {
                    Reachable = true,
                    DryRun = !string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase),
                    Message = "All matching candidates are already present."
                };
            }

            if (!string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase))
            {
                var names = string.Join(", ", candidates.Select(x => x["name"]?.GetValue<string>() ?? x["implementation"]?.GetValue<string>() ?? "unknown"));
                await _auditLogService.WriteAsync(
                    action: "AutoAddPolicyDryRun",
                    mode: configuration.Mode,
                    succeeded: true,
                    details: $"{trigger}: planned add for {names}.",
                    cancellationToken: cancellationToken);
                return new AutoAddRunResult
                {
                    Reachable = true,
                    DryRun = true,
                    CandidateCount = candidates.Count,
                    Message = $"Planned add for {names}."
                };
            }

            var addedCount = 0;
            var failedCount = 0;
            foreach (var candidate in candidates)
            {
                var preparedCandidate = PrepareSchemaForCreation(candidate, defaultAppProfileId, configuration, availableTags);
                var name = preparedCandidate["name"]?.GetValue<string>() ?? preparedCandidate["implementation"]?.GetValue<string>() ?? "unknown";
                var definitionName = preparedCandidate["definitionName"]?.GetValue<string>() ?? string.Empty;
                var result = await _apiClient.CreateIndexerAsync(configuration, preparedCandidate, cancellationToken);
                if (result.Success)
                {
                    addedCount++;
                    await ClearAutoAddFailureAsync(name, cancellationToken);
                }
                else
                {
                    failedCount++;
                    await RecordAutoAddFailureAsync(name, definitionName, result.Message, configuration.AutoAddFailureCooldownHours, cancellationToken);
                }

                await _auditLogService.WriteAsync(
                    action: "AutoAddPolicy",
                    mode: configuration.Mode,
                    succeeded: result.Success,
                    details: $"{trigger}: {result.Message}",
                    indexerName: name,
                    cancellationToken: cancellationToken);
            }

            if (addedCount > 0)
            {
                await _notificationDispatchService.NotifyAsync(
                    NotificationEvent.IndexerAutoAdded,
                    string.Format(T(configuration.Language, "NotifyMessageIndexerAutoAdded"), addedCount, candidates.Count),
                    cancellationToken);
            }

            return new AutoAddRunResult
            {
                Reachable = true,
                DryRun = false,
                CandidateCount = candidates.Count,
                AddedCount = addedCount,
                FailedCount = failedCount,
                Message = string.Format(T(configuration.Language, "AutoAddProcessedSummary"), candidates.Count, addedCount, failedCount)
            };
        }
        catch (Exception ex)
        {
            await _auditLogService.WriteAsync(
                action: string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase) ? "AutoAddPolicy" : "AutoAddPolicyDryRun",
                mode: configuration.Mode,
                succeeded: false,
                details: $"{trigger}: {ex.Message}",
                cancellationToken: cancellationToken);

            return new AutoAddRunResult
            {
                Reachable = false,
                DryRun = !string.Equals(configuration.Mode, "Apply", StringComparison.OrdinalIgnoreCase),
                Message = ex.Message
            };
        }
    }

    public async Task<int> GetAutoAddCooldownCountAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var retryTimes = await _dbContext.AutoAddFailedCandidates
            .AsNoTracking()
            .Select(x => x.NextRetryAtUtc)
            .ToListAsync(cancellationToken);
        return retryTimes.Count(retryAt => retryAt > now);
    }

    public async Task<IReadOnlyList<AutoAddCooldownEntryViewModel>> GetAutoAddCooldownEntriesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = await _dbContext.AutoAddFailedCandidates
            .AsNoTracking()
            .Select(x => new AutoAddCooldownEntryViewModel
            {
                Id = x.Id,
                Name = x.Name,
                DefinitionName = x.DefinitionName,
                FailureCount = x.FailureCount,
                NextRetryAtUtc = x.NextRetryAtUtc
            })
            .ToListAsync(cancellationToken);
        return entries
            .Where(entry => entry.NextRetryAtUtc > now)
            .OrderBy(entry => entry.NextRetryAtUtc)
            .ToList();
    }

    public async Task<bool> RemoveAutoAddCooldownAsync(int id, CancellationToken cancellationToken = default)
    {
        var entry = await _dbContext.AutoAddFailedCandidates.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        _dbContext.AutoAddFailedCandidates.Remove(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ClearAutoAddCooldownAsync(CancellationToken cancellationToken = default)
    {
        var count = await _dbContext.AutoAddFailedCandidates.CountAsync(cancellationToken);
        if (count == 0)
        {
            return;
        }

        await _dbContext.AutoAddFailedCandidates.ExecuteDeleteAsync(cancellationToken);

        var configuration = await _configurationService.GetAsync(cancellationToken);
        await _auditLogService.WriteAsync(
            action: "AutoAddCooldownCleared",
            mode: configuration?.Mode ?? "DryRun",
            succeeded: true,
            details: $"Svuotati manualmente {count} indexer dalla lista cooldown.",
            cancellationToken: cancellationToken);
    }

    private async Task RecordAutoAddFailureAsync(string name, string definitionName, string error, int cooldownHours, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.AutoAddFailedCandidates.SingleOrDefaultAsync(x => x.Name == name, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (entry is null)
        {
            entry = new AutoAddFailedCandidateEntity { Name = name };
            _dbContext.AutoAddFailedCandidates.Add(entry);
            entry.FailureCount = 0;
        }

        entry.DefinitionName = definitionName;
        entry.LastError = string.IsNullOrWhiteSpace(error) ? "Unknown error." : error;
        entry.FailureCount += 1;
        entry.LastAttemptAtUtc = now;
        // Nessun tentativo di riaggiunta per la finestra configurata: evita di ripetere
        // ad ogni ciclo login falliti che, su alcuni tracker, rischiano di far scattare ban temporanei.
        entry.NextRetryAtUtc = now.AddHours(Math.Max(1, cooldownHours));

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearAutoAddFailureAsync(string name, CancellationToken cancellationToken)
    {
        var entry = await _dbContext.AutoAddFailedCandidates.SingleOrDefaultAsync(x => x.Name == name, cancellationToken);
        if (entry is not null)
        {
            _dbContext.AutoAddFailedCandidates.Remove(entry);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool MatchesAutoAddFilters(JsonObject schema, SetupDraft configuration)
        => MatchesAutoAddFilters(schema, configuration, requireProtocol: true, requirePrivacy: true, requireLanguage: true);

    private static bool MatchesAutoAddFilters(JsonObject schema, SetupDraft configuration, bool requireProtocol, bool requirePrivacy, bool requireLanguage)
    {
        var protocolFilter = configuration.AutoAddProtocolFilter.Trim().ToLowerInvariant();
        var privacyFilters = ParsePrivacyFilters(configuration.AutoAddPrivacyFilter);
        var languageFilter = configuration.AutoAddLanguageFilter.Trim().ToLowerInvariant();
        var requestedCategories = ParseCategoryIds(configuration.AutoAddCategoryFilter);

        if (requireProtocol && protocolFilter != "all")
        {
            var protocol = schema["protocol"]?.GetValue<string>()?.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(protocol)
                && !string.Equals(protocol, protocolFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (requirePrivacy && privacyFilters.Count > 0)
        {
            var privacy = NormalizePrivacyToken(
                schema["privacy"]?.GetValue<string>()
                ?? schema["privacyLevel"]?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(privacy)
                && !MatchesPrivacyFilter(privacy, privacyFilters))
            {
                return false;
            }
        }

        if (!requireLanguage || string.IsNullOrWhiteSpace(languageFilter) || languageFilter == "all")
        {
            return requestedCategories.Count == 0 || MatchesCategoryFilter(schema, requestedCategories);
        }

        var allowed = languageFilter
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLanguageToken)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allowed.Count == 0)
        {
            return true;
        }

        var requiresAllLanguages = string.Equals(configuration.AutoAddLanguageMatchMode, "all", StringComparison.OrdinalIgnoreCase);
        var schemaLanguage = NormalizeLanguageToken(schema["language"]?.GetValue<string>());
        if (!string.IsNullOrWhiteSpace(schemaLanguage))
        {
            var languageMatched = requiresAllLanguages
                ? allowed.All(x => string.Equals(x, schemaLanguage, StringComparison.OrdinalIgnoreCase))
                : allowed.Contains(schemaLanguage, StringComparer.OrdinalIgnoreCase);
            return languageMatched && (requestedCategories.Count == 0 || MatchesCategoryFilter(schema, requestedCategories));
        }

        var languages = schema["languages"] as JsonArray;
        if (languages is null)
        {
            return configuration.AutoAddAllowUnknownLanguage
                && (requestedCategories.Count == 0 || MatchesCategoryFilter(schema, requestedCategories));
        }

        var available = languages.OfType<JsonValue>()
            .Select(x => x.TryGetValue<string>(out var value) ? value : string.Empty)
            .Select(NormalizeLanguageToken)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (available.Count == 0)
        {
            return configuration.AutoAddAllowUnknownLanguage
                && (requestedCategories.Count == 0 || MatchesCategoryFilter(schema, requestedCategories));
        }

        var matchesLanguage = requiresAllLanguages
            ? allowed.All(x => available.Contains(x, StringComparer.OrdinalIgnoreCase))
            : available.Any(x => allowed.Contains(x, StringComparer.OrdinalIgnoreCase));
        return matchesLanguage && (requestedCategories.Count == 0 || MatchesCategoryFilter(schema, requestedCategories));
    }

    private static string NormalizeLanguageToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        var separatorIndex = normalized.IndexOfAny(['-', '_']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return normalized switch
        {
            "eng" => "en",
            "english" => "en",
            "ita" => "it",
            "italian" => "it",
            _ => normalized
        };
    }

    private static string NormalizePrivacyToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace("_", "-");
        return normalized switch
        {
            "normal" => "semi-private",
            "semiprivate" => "semi-private",
            "members" => "semi-private",
            "login" => "semi-private",
            _ => normalized
        };
    }

    private static List<string> ParsePrivacyFilters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var filters = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePrivacyToken)
            .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "all", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (filters.Count == 0 || filters.Count == 3)
        {
            return [];
        }

        return filters;
    }

    private static bool MatchesPrivacyFilter(string schemaPrivacy, IReadOnlyCollection<string> configuredFilters)
    {
        if (configuredFilters.Count == 0)
        {
            return true;
        }

        var privacy = NormalizePrivacyToken(schemaPrivacy);

        return configuredFilters.Contains(privacy, StringComparer.OrdinalIgnoreCase);
    }

    private static List<int> ParseCategoryIds(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var parsed) ? parsed : 0)
                .Where(x => x > 0)
                .Distinct()
                .ToList();

    private static bool MatchesCategoryFilter(JsonObject schema, IReadOnlyCollection<int> requestedCategories)
    {
        if (requestedCategories.Count == 0)
        {
            return true;
        }

        var available = new HashSet<int>();
        CollectCategoryIds(schema["categories"], available);
        CollectCategoryIds(schema["capabilities"]?["categories"], available);
        if (available.Count == 0 && schema["fields"] is JsonArray fields)
        {
            CollectCategoryIdsFromFields(fields, available);
        }

        return available.Overlaps(requestedCategories);
    }

    private static void CollectCategoryIds(JsonNode? categories, ISet<int> target)
    {
        if (categories is null)
        {
            return;
        }

        if (categories is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue) && intValue > 0)
            {
                target.Add(intValue);
            }

            if (value.TryGetValue<string>(out var stringValue)
                && int.TryParse(stringValue, out var parsedValue)
                && parsedValue > 0)
            {
                target.Add(parsedValue);
            }

            return;
        }

        if (categories is JsonObject categoryObject)
        {
            var id = categoryObject["id"]?.GetValue<int>()
                ?? categoryObject["value"]?.GetValue<int>()
                ?? 0;
            if (id > 0)
            {
                target.Add(id);
            }

            CollectCategoryIds(categoryObject["value"], target);
            CollectCategoryIds(categoryObject["subCategories"], target);
            CollectCategoryIds(categoryObject["children"], target);
            return;
        }

        if (categories is not JsonArray categoryArray)
        {
            return;
        }

        foreach (var item in categoryArray)
        {
            CollectCategoryIds(item, target);
        }
    }

    private static void CollectCategoryIdsFromFields(JsonArray fields, ISet<int> target)
    {
        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            var isCategoriesField = IsMatchingFieldName(name, "categories");
            var categoryId = 0;
            if (!isCategoriesField && !TryParseCategoryIdFromFieldName(name, out categoryId))
            {
                continue;
            }

            if (!isCategoriesField && categoryId > 0)
            {
                target.Add(categoryId);
            }

            CollectCategoryIds(field["value"], target);
            CollectCategoryIds(field["defaultValue"], target);
            CollectCategoryIds(field["selectOptions"], target);
        }
    }

    private static IEnumerable<JsonObject> SelectAutoAddCandidates(IReadOnlyList<JsonObject> schemas, IReadOnlyList<ProwlarrIndexerDto> existingIndexers, IReadOnlyList<dynamic> blockedIndexers, IReadOnlyList<dynamic> recentlyFailedIndexers, SetupDraft configuration)
    {
        static bool NotExisting(JsonObject schema, IReadOnlyList<ProwlarrIndexerDto> existing)
        {
            var name = schema["name"]?.GetValue<string>() ?? schema["implementation"]?.GetValue<string>() ?? string.Empty;
            return !existing.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        static bool NotBlocked(JsonObject schema, IReadOnlyList<dynamic> blocked)
        {
            var name = schema["name"]?.GetValue<string>() ?? schema["implementation"]?.GetValue<string>() ?? string.Empty;
            var definitionName = schema["definitionName"]?.GetValue<string>() ?? string.Empty;
            return !blocked.Any(x =>
                (!string.IsNullOrWhiteSpace((string?)x.Name) && string.Equals((string?)x.Name, name, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace((string?)x.DefinitionName) && string.Equals((string?)x.DefinitionName, definitionName, StringComparison.OrdinalIgnoreCase)));
        }

        static bool NotRecentlyFailed(JsonObject schema, IReadOnlyList<dynamic> recentlyFailed)
        {
            var name = schema["name"]?.GetValue<string>() ?? schema["implementation"]?.GetValue<string>() ?? string.Empty;
            var definitionName = schema["definitionName"]?.GetValue<string>() ?? string.Empty;
            return !recentlyFailed.Any(x =>
                (!string.IsNullOrWhiteSpace((string?)x.Name) && string.Equals((string?)x.Name, name, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace((string?)x.DefinitionName) && string.Equals((string?)x.DefinitionName, definitionName, StringComparison.OrdinalIgnoreCase)));
        }

        var strict = schemas.Where(x => MatchesAutoAddFilters(x, configuration, requireProtocol: true, requirePrivacy: true, requireLanguage: true) && NotExisting(x, existingIndexers) && NotBlocked(x, blockedIndexers) && NotRecentlyFailed(x, recentlyFailedIndexers) && CanSatisfyCredentialRequirements(x, configuration)).ToList();
        if (strict.Count > 0)
        {
            return strict;
        }

        var hasLanguageFilter = !string.IsNullOrWhiteSpace(configuration.AutoAddLanguageFilter)
            && !string.Equals(configuration.AutoAddLanguageFilter.Trim(), "all", StringComparison.OrdinalIgnoreCase);
        if (hasLanguageFilter)
        {
            var relaxedPrivacyOnly = schemas.Where(x => MatchesAutoAddFilters(x, configuration, requireProtocol: true, requirePrivacy: false, requireLanguage: true) && NotExisting(x, existingIndexers) && NotBlocked(x, blockedIndexers) && NotRecentlyFailed(x, recentlyFailedIndexers) && CanSatisfyCredentialRequirements(x, configuration)).ToList();
            if (relaxedPrivacyOnly.Count > 0)
            {
                return relaxedPrivacyOnly;
            }

            return [];
        }

        var relaxedLanguage = schemas.Where(x => MatchesAutoAddFilters(x, configuration, requireProtocol: true, requirePrivacy: true, requireLanguage: false) && NotExisting(x, existingIndexers) && NotBlocked(x, blockedIndexers) && NotRecentlyFailed(x, recentlyFailedIndexers) && CanSatisfyCredentialRequirements(x, configuration)).ToList();
        if (relaxedLanguage.Count > 0)
        {
            return relaxedLanguage;
        }

        var relaxedPrivacy = schemas.Where(x => MatchesAutoAddFilters(x, configuration, requireProtocol: true, requirePrivacy: false, requireLanguage: false) && NotExisting(x, existingIndexers) && NotBlocked(x, blockedIndexers) && NotRecentlyFailed(x, recentlyFailedIndexers) && CanSatisfyCredentialRequirements(x, configuration)).ToList();
        if (relaxedPrivacy.Count > 0)
        {
            return relaxedPrivacy;
        }

        return schemas.Where(x => MatchesAutoAddFilters(x, configuration, requireProtocol: false, requirePrivacy: false, requireLanguage: false) && NotExisting(x, existingIndexers) && NotBlocked(x, blockedIndexers) && NotRecentlyFailed(x, recentlyFailedIndexers) && CanSatisfyCredentialRequirements(x, configuration)).ToList();
    }

    private static JsonObject PrepareSchemaForCreation(JsonObject schema, int defaultAppProfileId, SetupDraft configuration, IReadOnlyList<ProwlarrTagOption> availableTags)
    {
        var clone = JsonNode.Parse(schema.ToJsonString())?.AsObject() ?? new JsonObject();
        clone["enable"] = true;

        var appProfileId = clone["appProfileId"]?.GetValue<int>() ?? 0;
        if (appProfileId <= 0)
        {
            appProfileId = ResolveAppProfileIdFromFields(clone) ?? defaultAppProfileId;
        }

        if (appProfileId > 0)
        {
            clone["appProfileId"] = appProfileId;
            SetFieldValue(clone, "appProfileId", JsonValue.Create(appProfileId));
            SetFieldValue(clone, "appProfile", JsonValue.Create(appProfileId));
        }

        ApplyGlobalCredentialSettings(clone, configuration);
        ApplyDefaultIndexerSettings(clone, configuration, availableTags);

        return clone;
    }

    private static int? ResolveAppProfileIdFromFields(JsonObject schema)
    {
        if (schema["fields"] is not JsonArray fields)
        {
            return null;
        }

        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            if (!string.Equals(name, "appProfileId", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "appProfile", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = field["value"]?.GetValue<int>() ?? 0;
            if (value > 0)
            {
                return value;
            }

            if (field["selectOptions"] is not JsonArray selectOptions)
            {
                continue;
            }

            foreach (var option in selectOptions.OfType<JsonObject>())
            {
                var optionValue = option["value"]?.GetValue<int>() ?? 0;
                if (optionValue > 0)
                {
                    return optionValue;
                }
            }
        }

        return null;
    }

    private static void SetFieldValue(JsonObject schema, string fieldName, JsonNode? value)
    {
        if (schema["fields"] is not JsonArray fields)
        {
            return;
        }

        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            if (IsMatchingFieldName(name, fieldName))
            {
                field["value"] = value?.DeepClone();
            }
        }
    }

    private static bool TryGetBaseUrlCandidates(JsonObject payload, out string currentBaseUrl, out List<string> candidates)
    {
        currentBaseUrl = payload["baseUrl"]?.GetValue<string>() ?? string.Empty;
        candidates = new List<string>();

        if (payload["fields"] is not JsonArray fields)
        {
            return false;
        }

        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            if (!string.Equals(name, "baseUrl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            currentBaseUrl = field["value"]?.GetValue<string>() ?? currentBaseUrl;
            if (!string.IsNullOrWhiteSpace(currentBaseUrl))
            {
                candidates.Add(currentBaseUrl);
            }

            if (field["selectOptions"] is JsonArray options)
            {
                foreach (var option in options.OfType<JsonObject>())
                {
                    var value = option["value"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            break;
        }

        candidates = candidates
            .Where(x => Uri.TryCreate(x, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count > 1 || (!string.IsNullOrWhiteSpace(currentBaseUrl) && candidates.Count == 1);
    }

    private static void SetBaseUrl(JsonObject payload, string baseUrl)
    {
        payload["baseUrl"] = baseUrl;
        SetFieldValue(payload, "baseUrl", JsonValue.Create(baseUrl));
    }

    private static void ApplyDefaultIndexerSettings(JsonObject schema, SetupDraft configuration, IReadOnlyList<ProwlarrTagOption> availableTags)
    {
        SetOptionalNumericField(schema, ["queryLimit"], configuration.AutoAddDefaultQueryLimit);
        SetOptionalNumericField(schema, ["grabLimit"], configuration.AutoAddDefaultGrabLimit);
        SetOptionalSelectField(schema, ["limitsUnit"], configuration.AutoAddDefaultLimitsUnit);
        SetOptionalNumericField(schema, ["appMinimumSeeders", "minimumSeeders"], configuration.AutoAddDefaultAppMinimumSeeders);
        SetOptionalDecimalField(schema, ["seedRatio"], configuration.AutoAddDefaultSeedRatio);
        SetOptionalNumericField(schema, ["seedTime"], configuration.AutoAddDefaultSeedTime);
        SetOptionalNumericField(schema, ["packSeedTime"], configuration.AutoAddDefaultPackSeedTime);
        SetOptionalBooleanModeField(schema, ["preferMagnetUrl"], configuration.AutoAddDefaultPreferMagnetUrlMode);
        SetOptionalNumericField(schema, ["indexerPriority", "priority"], configuration.AutoAddDefaultIndexerPriority);
        SetOptionalSelectField(schema, ["downloadClientId", "downloadClient"], configuration.AutoAddDefaultDownloadClient);
        SetOptionalStringField(schema, ["filterByUploader", "uploader"], configuration.AutoAddDefaultFilterByUploader);
        SetOptionalTagsField(schema, configuration.AutoAddDefaultTags, availableTags);
    }

    private static void ApplyGlobalCredentialSettings(JsonObject schema, SetupDraft configuration)
    {
        SetOptionalStringField(schema, ["username", "user"], configuration.AutoAddGlobalUsername);
        SetOptionalStringField(schema, ["password", "pass"], configuration.AutoAddGlobalPassword);
    }

    private static bool CanSatisfyCredentialRequirements(JsonObject schema, SetupDraft configuration)
    {
        if (schema["fields"] is not JsonArray fields)
        {
            return true;
        }

        var names = fields
            .OfType<JsonObject>()
            .Select(x => x["name"]?.GetValue<string>()?.Trim().ToLowerInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if ((names.Contains("username") || names.Contains("user")) && string.IsNullOrWhiteSpace(configuration.AutoAddGlobalUsername))
        {
            return false;
        }

        if ((names.Contains("password") || names.Contains("pass")) && string.IsNullOrWhiteSpace(configuration.AutoAddGlobalPassword))
        {
            return false;
        }

        var unsupportedSecrets = new[] { "apikey", "api-key", "passkey", "cookie", "rsskey", "authkey", "pid", "pin", "mamid", "totp", "twofactor", "2fa" };
        return !unsupportedSecrets.Any(names.Contains);
    }

    private static void SetOptionalNumericField(JsonObject schema, IReadOnlyList<string> aliases, int? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        var preferredAlias = ResolvePreferredAlias(schema, aliases);
        schema[preferredAlias] = value.Value;
        SetFieldValue(schema, preferredAlias, JsonValue.Create(value.Value));
        SyncAliasFieldValues(schema, aliases, JsonValue.Create(value.Value));
    }

    private static void SetOptionalDecimalField(JsonObject schema, IReadOnlyList<string> aliases, decimal? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        var preferredAlias = ResolvePreferredAlias(schema, aliases);
        schema[preferredAlias] = value.Value;
        SetFieldValue(schema, preferredAlias, JsonValue.Create(value.Value));
        SyncAliasFieldValues(schema, aliases, JsonValue.Create(value.Value));
    }

    private static void SetOptionalStringField(JsonObject schema, IReadOnlyList<string> aliases, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmedValue = value.Trim();
        var preferredAlias = ResolvePreferredAlias(schema, aliases);
        schema[preferredAlias] = trimmedValue;
        SetFieldValue(schema, preferredAlias, JsonValue.Create(trimmedValue));
        SyncAliasFieldValues(schema, aliases, JsonValue.Create(trimmedValue));
    }

    private static void SetOptionalBooleanModeField(JsonObject schema, IReadOnlyList<string> aliases, string? mode)
    {
        if (string.Equals(mode, "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var value = string.Equals(mode, "enable", StringComparison.OrdinalIgnoreCase);
        var preferredAlias = ResolvePreferredAlias(schema, aliases);
        schema[preferredAlias] = value;
        SetFieldValue(schema, preferredAlias, JsonValue.Create(value));
        SyncAliasFieldValues(schema, aliases, JsonValue.Create(value));
    }

    private static void SetOptionalSelectField(JsonObject schema, IReadOnlyList<string> aliases, string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return;
        }

        foreach (var alias in aliases)
        {
            if (TryResolveSelectOptionValue(schema, alias, configuredValue.Trim(), out var optionValue))
            {
                var resolved = optionValue?.DeepClone();
                schema[alias] = resolved;
                SetFieldValue(schema, alias, resolved);
                SyncAliasFieldValues(schema, aliases, resolved);
                return;
            }
        }

        var fallbackValue = TryCreateScalarValue(configuredValue.Trim());
        var preferredAlias = ResolvePreferredAlias(schema, aliases);
        schema[preferredAlias] = fallbackValue;
        SetFieldValue(schema, preferredAlias, fallbackValue?.DeepClone());
        SyncAliasFieldValues(schema, aliases, fallbackValue?.DeepClone());
    }

    private static void SetOptionalTagsField(JsonObject schema, string? configuredTags, IReadOnlyList<ProwlarrTagOption> availableTags)
    {
        if (string.IsNullOrWhiteSpace(configuredTags))
        {
            return;
        }

        var requested = configuredTags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (requested.Count == 0)
        {
            return;
        }

        var values = new JsonArray();

        if (schema["fields"] is JsonArray fields)
        {
            foreach (var field in fields.OfType<JsonObject>())
            {
                var name = field["name"]?.GetValue<string>();
                if (!IsMatchingFieldName(name, "tags") || field["selectOptions"] is not JsonArray options)
                {
                    continue;
                }

                foreach (var tag in requested)
                {
                    foreach (var option in options.OfType<JsonObject>())
                    {
                        var optionLabel = GetSelectOptionLabel(option);
                        if (!string.Equals(optionLabel, tag, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (option["value"] is { } optionValue)
                        {
                            values.Add(optionValue.DeepClone());
                        }
                        break;
                    }
                }

                if (values.Count > 0)
                {
                    field["value"] = values.DeepClone();
                    break;
                }
            }
        }

        if (values.Count == 0)
        {
            foreach (var tag in requested)
            {
                var matched = availableTags.FirstOrDefault(option => string.Equals(option.Label, tag, StringComparison.OrdinalIgnoreCase));
                if (matched is null || matched.Id <= 0)
                {
                    continue;
                }

                values.Add(JsonValue.Create(matched.Id));
            }
        }

        if (values.Count == 0)
        {
            return;
        }

        schema["tags"] = values;
        SetFieldValue(schema, "tags", values.DeepClone());
    }

    private static bool TryResolveSelectOptionValue(JsonObject schema, string fieldName, string configuredValue, out JsonNode? resolvedValue)
    {
        resolvedValue = null;
        if (schema["fields"] is not JsonArray fields)
        {
            return false;
        }

        foreach (var field in fields.OfType<JsonObject>())
        {
            var name = field["name"]?.GetValue<string>();
            if (!IsMatchingFieldName(name, fieldName) || field["selectOptions"] is not JsonArray options)
            {
                continue;
            }

            foreach (var option in options.OfType<JsonObject>())
            {
                var optionLabel = GetSelectOptionLabel(option);
                var optionValueText = option["value"]?.ToJsonString().Trim('"') ?? string.Empty;
                if (!string.Equals(optionLabel, configuredValue, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(optionValueText, configuredValue, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                resolvedValue = option["value"]?.DeepClone();
                return resolvedValue is not null;
            }
        }

        return false;
    }

    private static string GetSelectOptionLabel(JsonObject option)
        => option["name"]?.GetValue<string>()
            ?? option["label"]?.GetValue<string>()
            ?? option["key"]?.GetValue<string>()
            ?? string.Empty;

    private static string ResolvePreferredAlias(JsonObject schema, IReadOnlyList<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (HasField(schema, alias))
            {
                return alias;
            }
        }

        return aliases[0];
    }

    private static void SyncAliasFieldValues(JsonObject schema, IReadOnlyList<string> aliases, JsonNode? value)
    {
        foreach (var alias in aliases)
        {
            SetFieldValue(schema, alias, value?.DeepClone());
        }
    }

    private static JsonNode? TryCreateScalarValue(string configuredValue)
    {
        if (int.TryParse(configuredValue, out var intValue))
        {
            return JsonValue.Create(intValue);
        }

        if (decimal.TryParse(configuredValue, out var decimalValue))
        {
            return JsonValue.Create(decimalValue);
        }

        if (bool.TryParse(configuredValue, out var boolValue))
        {
            return JsonValue.Create(boolValue);
        }

        return JsonValue.Create(configuredValue);
    }

    private static bool HasField(JsonObject schema, string fieldName)
    {
        if (schema[fieldName] is not null)
        {
            return true;
        }

        if (schema["fields"] is not JsonArray fields)
        {
            return false;
        }

        return fields.OfType<JsonObject>()
            .Any(field => IsMatchingFieldName(field["name"]?.GetValue<string>(), fieldName));
    }

    private static bool IsMatchingFieldName(string? actualName, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(actualName) || string.IsNullOrWhiteSpace(expectedName))
        {
            return false;
        }

        if (string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return actualName.EndsWith($".{expectedName}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCategoryIdFromFieldName(string? fieldName, out int categoryId)
    {
        categoryId = 0;
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        const string marker = "category_";
        var index = fieldName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var suffix = fieldName[(index + marker.Length)..];
        return int.TryParse(suffix, out categoryId) && categoryId > 0;
    }

    private static string T(string? language, string key)
        => Indexarr.Web.Localization.UiTextCatalog.Get(language, key);
}
