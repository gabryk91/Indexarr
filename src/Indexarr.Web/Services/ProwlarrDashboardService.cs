using Indexarr.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Indexarr.Web.Services;

public sealed class ProwlarrDashboardService
{
    private const int IndexerPageSize = 10;
    private const int AuditPageSize = 15;
    private readonly ProwlarrApiClient _apiClient;
    private readonly Indexarr.Web.Data.IndexarrDbContext _dbContext;
    private readonly IndexerAutomationService _automationService;

    public ProwlarrDashboardService(ProwlarrApiClient apiClient, Indexarr.Web.Data.IndexarrDbContext dbContext, IndexerAutomationService automationService)
    {
        _apiClient = apiClient;
        _dbContext = dbContext;
        _automationService = automationService;
    }

    public async Task<ProwlarrDashboardViewModel> BuildAsync(SetupDraft configuration, DashboardBuildRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new DashboardBuildRequest();
        var auditDateFilter = NormalizeAuditDateFilter(request.AuditDateFilter, configuration.Timezone);
        var auditActionFilter = NormalizeAuditActionFilter(request.AuditActionFilter);
        var auditResultFilter = NormalizeAuditResultFilter(request.AuditResultFilter);

        var states = await _dbContext.IndexerStates.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var blockedIndexers = await _dbContext.BlockedIndexers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var history = (await _dbContext.IndexerHealthChecks
            .AsNoTracking()
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CheckedAtUtc)
            .Take(500)
            .ToList();

        var latestRunAt = history.Count == 0 ? (DateTimeOffset?)null : history.Max(x => x.CheckedAtUtc);
        var allAuditLogs = (await _dbContext.AuditLogs
            .AsNoTracking()
            .Select(x => new AuditLogViewModel
            {
                Id = x.Id,
                Action = x.Action,
                Mode = x.Mode,
                IndexerName = x.IndexerName,
                Succeeded = x.Succeeded,
                Details = x.Details,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
        var availableAuditActions = allAuditLogs
            .Select(x => x.Action)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        var auditLogs = allAuditLogs
            .Where(x => MatchesAuditDate(x, auditDateFilter, configuration.Timezone))
            .Where(x => MatchesAuditAction(x, auditActionFilter))
            .Where(x => MatchesAuditResult(x, auditResultFilter))
            .ToList();

        var liveStates = states.Where(x => !x.IsBlocked).ToList();
        var blockedViewModels = blockedIndexers.Select(blocked =>
        {
            var state = states.FirstOrDefault(x => x.IndexerId == blocked.OriginalIndexerId);
            return new IndexerHealthViewModel
            {
                Id = blocked.OriginalIndexerId,
                BlockedIndexerId = blocked.Id,
                Name = blocked.Name,
                Enabled = false,
                Protocol = blocked.Protocol,
                Implementation = blocked.Implementation,
                Result = "Blocked",
                Error = "Blocked by user.",
                LatencyMs = state?.LastLatencyMs ?? 0,
                LastCheckedAtUtc = state?.LastCheckedAtUtc,
                ConsecutiveFailures = state?.ConsecutiveFailures ?? 0,
                Trend = "Flat",
                IsBlocked = true
            };
        }).ToList();

        var allIndexers = liveStates.Select(state =>
        {
            var recent = history.Where(x => x.IndexerId == state.IndexerId).OrderByDescending(x => x.CheckedAtUtc).Take(3).ToList();
            return new IndexerHealthViewModel
            {
                Id = state.IndexerId,
                Name = state.Name,
                Enabled = state.Enabled,
                Protocol = state.Protocol,
                Implementation = state.Implementation,
                Result = string.IsNullOrWhiteSpace(state.LastResult) ? "Unknown" : state.LastResult,
                Error = state.LastError,
                LatencyMs = state.LastLatencyMs,
                LastCheckedAtUtc = state.LastCheckedAtUtc,
                ConsecutiveFailures = state.ConsecutiveFailures,
                Trend = CalculateTrend(recent)
            };
        }).Concat(blockedViewModels).ToList();

        var filtered = allIndexers.Where(x => MatchesStatus(x, request.StatusFilter) && MatchesProtocol(x, request.ProtocolFilter)).ToList();
        var orderedFiltered = OrderIndexers(filtered);
        var indexerTotalCount = orderedFiltered.Count;
        var indexerPage = ClampPage(request.IndexerPage, indexerTotalCount, IndexerPageSize);
        var auditTotalCount = auditLogs.Count;
        var auditPage = ClampPage(request.AuditPage, auditTotalCount, AuditPageSize);

        try
        {
            var status = await _apiClient.GetSystemStatusAsync(configuration, cancellationToken);
            var currentIndexers = await _apiClient.GetIndexersAsync(configuration, cancellationToken);
            var currentIndexerIds = currentIndexers.Select(x => x.Id).ToHashSet();
            await RemoveMissingIndexerStatesAsync(currentIndexerIds, blockedIndexers.Select(x => x.OriginalIndexerId).ToHashSet(), cancellationToken);

            allIndexers = currentIndexers.Select(indexer =>
            {
                var state = states.FirstOrDefault(x => x.IndexerId == indexer.Id);
                var recent = history.Where(x => x.IndexerId == indexer.Id).OrderByDescending(x => x.CheckedAtUtc).Take(3).ToList();

                return new IndexerHealthViewModel
                {
                    Id = indexer.Id,
                    Name = indexer.Name ?? state?.Name ?? $"Indexer {indexer.Id}",
                    Enabled = indexer.Enable,
                    Protocol = indexer.Protocol ?? state?.Protocol ?? "-",
                    Implementation = indexer.Implementation ?? state?.Implementation ?? "-",
                    Result = string.IsNullOrWhiteSpace(state?.LastResult) ? (indexer.Enable ? "Unknown" : "Disabled") : state.LastResult,
                    Error = state?.LastError ?? string.Empty,
                    LatencyMs = state?.LastLatencyMs ?? 0,
                    LastCheckedAtUtc = state?.LastCheckedAtUtc,
                    ConsecutiveFailures = state?.ConsecutiveFailures ?? 0,
                    Trend = CalculateTrend(recent)
                };
            }).Concat(blockedViewModels).ToList();

            filtered = allIndexers.Where(x => MatchesStatus(x, request.StatusFilter) && MatchesProtocol(x, request.ProtocolFilter)).ToList();
            orderedFiltered = OrderIndexers(filtered);
            indexerTotalCount = orderedFiltered.Count;
            indexerPage = ClampPage(request.IndexerPage, indexerTotalCount, IndexerPageSize);

            return new ProwlarrDashboardViewModel
            {
                Reachable = true,
                InstanceName = status.InstanceName ?? "Prowlarr",
                Version = status.Version ?? "-",
                TotalIndexers = currentIndexers.Count,
                EnabledIndexers = currentIndexers.Count(x => x.Enable),
                HealthyIndexers = currentIndexers.Count(x => states.Any(s => s.IndexerId == x.Id && s.LastResult == "OK")),
                FailedIndexers = currentIndexers.Count(x => states.Any(s => s.IndexerId == x.Id && s.LastResult == "FAIL")),
                BlockedIndexers = blockedViewModels.Count,
                LastRunAtUtc = latestRunAt,
                AvailableProtocols = allIndexers.Select(x => x.Protocol).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                AvailableAuditActions = availableAuditActions,
                Filters = new DashboardFilterModel
                {
                    Status = request.StatusFilter,
                    Protocol = request.ProtocolFilter,
                    AuditDate = auditDateFilter,
                    AuditAction = auditActionFilter,
                    AuditResult = auditResultFilter
                },
                AuditLogs = Page(auditLogs, auditPage, AuditPageSize),
                Indexers = Page(orderedFiltered, indexerPage, IndexerPageSize),
                IndexerPage = indexerPage,
                IndexerPageSize = IndexerPageSize,
                IndexerTotalCount = indexerTotalCount,
                IndexerTotalPages = TotalPages(indexerTotalCount, IndexerPageSize),
                AuditPage = auditPage,
                AuditPageSize = AuditPageSize,
                AuditTotalCount = auditTotalCount,
                AuditTotalPages = TotalPages(auditTotalCount, AuditPageSize)
            };
        }
        catch (Exception ex)
        {
            return new ProwlarrDashboardViewModel
            {
                Reachable = false,
                ErrorMessage = ex.Message,
                TotalIndexers = allIndexers.Count,
                EnabledIndexers = allIndexers.Count(x => x.Enabled),
                HealthyIndexers = liveStates.Count(x => x.LastResult == "OK"),
                FailedIndexers = liveStates.Count(x => x.LastResult == "FAIL"),
                BlockedIndexers = blockedViewModels.Count,
                LastRunAtUtc = latestRunAt,
                AvailableProtocols = allIndexers.Select(x => x.Protocol).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                AvailableAuditActions = availableAuditActions,
                Filters = new DashboardFilterModel
                {
                    Status = request.StatusFilter,
                    Protocol = request.ProtocolFilter,
                    AuditDate = auditDateFilter,
                    AuditAction = auditActionFilter,
                    AuditResult = auditResultFilter
                },
                AuditLogs = Page(auditLogs, auditPage, AuditPageSize),
                Indexers = Page(orderedFiltered, indexerPage, IndexerPageSize),
                IndexerPage = indexerPage,
                IndexerPageSize = IndexerPageSize,
                IndexerTotalCount = indexerTotalCount,
                IndexerTotalPages = TotalPages(indexerTotalCount, IndexerPageSize),
                AuditPage = auditPage,
                AuditPageSize = AuditPageSize,
                AuditTotalCount = auditTotalCount,
                AuditTotalPages = TotalPages(auditTotalCount, AuditPageSize)
            };
        }
    }

    private async Task RemoveMissingIndexerStatesAsync(ISet<int> currentIndexerIds, ISet<int> blockedIndexerIds, CancellationToken cancellationToken)
    {
        var staleStates = await _dbContext.IndexerStates
            .Where(x => !currentIndexerIds.Contains(x.IndexerId) && !blockedIndexerIds.Contains(x.IndexerId))
            .ToListAsync(cancellationToken);

        if (staleStates.Count == 0)
        {
            return;
        }

        _dbContext.IndexerStates.RemoveRange(staleStates);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string CalculateTrend(IReadOnlyList<Indexarr.Web.Data.Entities.IndexerHealthCheckEntity> recent)
    {
        if (recent.Count == 0)
        {
            return "Flat";
        }

        if (string.Equals(recent[0].Result, "FAIL", StringComparison.OrdinalIgnoreCase))
        {
            return recent.Skip(1).Any(x => string.Equals(x.Result, "OK", StringComparison.OrdinalIgnoreCase)) ? "Down" : "Flat";
        }

        return recent.Skip(1).Any(x => string.Equals(x.Result, "FAIL", StringComparison.OrdinalIgnoreCase)) ? "Up" : "Flat";
    }

    private static bool MatchesStatus(IndexerHealthViewModel indexer, string status)
        => status switch
        {
            "ok" => indexer.Result == "OK",
            "fail" => indexer.Result == "FAIL",
            "disabled" => !indexer.Enabled || indexer.Result == "Disabled",
            "blocked" => indexer.IsBlocked,
            _ => true
        };

    private static bool MatchesProtocol(IndexerHealthViewModel indexer, string protocol)
        => string.Equals(protocol, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(indexer.Protocol, protocol, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<IndexerHealthViewModel> OrderIndexers(IEnumerable<IndexerHealthViewModel> indexers)
        => indexers.OrderBy(x => x.Result == "FAIL" ? 0 : 1).ThenBy(x => x.Name).ToList();

    private static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int page, int pageSize)
        => items.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    private static int ClampPage(int requestedPage, int totalCount, int pageSize)
        => Math.Min(Math.Max(1, requestedPage), Math.Max(1, TotalPages(totalCount, pageSize)));

    private static int TotalPages(int totalCount, int pageSize)
        => Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));

    private static string NormalizeAuditDateFilter(string? value, string? timezone)
    {
        if (!string.IsNullOrWhiteSpace(value) && DateOnly.TryParse(value, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd");
        }

        var zone = ResolveTimeZone(timezone);
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
        return DateOnly.FromDateTime(today).ToString("yyyy-MM-dd");
    }

    private static string NormalizeAuditActionFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim();

    private static string NormalizeAuditResultFilter(string? value)
        => value?.ToLowerInvariant() switch
        {
            "ok" => "ok",
            "fail" => "fail",
            _ => "all"
        };

    private static bool MatchesAuditDate(AuditLogViewModel entry, string auditDateFilter, string? timezone)
    {
        if (!DateOnly.TryParse(auditDateFilter, out var selectedDate))
        {
            return true;
        }

        var zone = ResolveTimeZone(timezone);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(entry.CreatedAtUtc, zone).DateTime);
        return localDate == selectedDate;
    }

    private static bool MatchesAuditAction(AuditLogViewModel entry, string action)
        => string.Equals(action, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Action, action, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAuditResult(AuditLogViewModel entry, string result)
        => result switch
        {
            "ok" => entry.Succeeded,
            "fail" => !entry.Succeeded,
            _ => true
        };

    private static TimeZoneInfo ResolveTimeZone(string? timezone)
    {
        if (!string.IsNullOrWhiteSpace(timezone))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timezone);
            }
            catch
            {
            }
        }

        return TimeZoneInfo.Local;
    }
}
