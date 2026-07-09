using Indexarr.Web.Localization;
using Indexarr.Web.Models;
using Indexarr.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Indexarr.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly AppConfigurationService _configurationService;
    private readonly ProwlarrDashboardService _dashboardService;
    private readonly IndexerAutomationService _automationService;

    public IndexModel(AppConfigurationService configurationService, ProwlarrDashboardService dashboardService, IndexerAutomationService automationService)
    {
        _configurationService = configurationService;
        _dashboardService = dashboardService;
        _automationService = automationService;
    }

    [BindProperty(SupportsGet = true)]
    public string StatusFilter { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string ProtocolFilter { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string AuditDateFilter { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string AuditActionFilter { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string AuditResultFilter { get; set; } = "all";

    [TempData]
    public string? FlashMessage { get; set; }

    public SetupDraft? SetupDraft { get; private set; }

    public string CurrentLanguage => UiTextCatalog.Normalize(SetupDraft?.Language);

    public bool CanManage => User.Identity?.IsAuthenticated == true;

    public ProwlarrDashboardViewModel Dashboard { get; private set; } = new();

    public async Task OnGetAsync()
    {
        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        ViewData["CurrentLanguage"] = CurrentLanguage;

        if (SetupDraft is null)
        {
            return;
        }

        Dashboard = await _dashboardService.BuildAsync(
            SetupDraft,
            new DashboardBuildRequest
            {
                StatusFilter = NormalizeStatusFilter(StatusFilter),
                ProtocolFilter = NormalizeProtocolFilter(ProtocolFilter),
                AuditDateFilter = NormalizeAuditDateFilter(AuditDateFilter),
                AuditActionFilter = NormalizeAuditActionFilter(AuditActionFilter),
                AuditResultFilter = NormalizeAuditResultFilter(AuditResultFilter)
            },
            HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostRunHealthCheckAsync()
    {
        if (!CanManage)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Index", BuildRouteValues()) });
        }

        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        var result = await _automationService.RunHealthChecksAsync("manual-dashboard", HttpContext.RequestAborted);
        FlashMessage = result.Reachable
            ? string.Format(T("HealthCheckCompleted"), result.HealthyIndexers, result.FailedIndexers)
            : string.Format(T("HealthCheckFailed"), result.ErrorMessage);
        return RedirectToPage(BuildRouteValues());
    }

    public async Task<IActionResult> OnPostRunAutoAddAsync()
    {
        if (!CanManage)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Index", BuildRouteValues()) });
        }

        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        var result = await _automationService.RunAutoAddAsync("manual-auto-add", HttpContext.RequestAborted);
        FlashMessage = result.Reachable
            ? result.Message
            : $"{T("OperationFailed")} {result.Message}";
        return RedirectToPage(BuildRouteValues());
    }

    public async Task<IActionResult> OnPostToggleIndexerAsync(int indexerId, bool enabled)
    {
        if (!CanManage)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Index", BuildRouteValues()) });
        }

        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        var result = await _automationService.SetIndexerEnabledAsync(indexerId, enabled, HttpContext.RequestAborted);
        FlashMessage = result.Success ? result.Message : $"{T("OperationFailed")} {result.Message}";
        return RedirectToPage(BuildRouteValues());
    }

    public async Task<IActionResult> OnPostBlockIndexerAsync(int indexerId)
    {
        if (!CanManage)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Index", BuildRouteValues()) });
        }

        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        var result = await _automationService.BlockIndexerAsync(indexerId, HttpContext.RequestAborted);
        FlashMessage = result.Success ? result.Message : $"{T("OperationFailed")} {result.Message}";
        return RedirectToPage(BuildRouteValues());
    }

    public async Task<IActionResult> OnPostUnblockIndexerAsync(int blockedIndexerId)
    {
        if (!CanManage)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Index", BuildRouteValues()) });
        }

        SetupDraft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        var result = await _automationService.UnblockIndexerAsync(blockedIndexerId, HttpContext.RequestAborted);
        FlashMessage = result.Success ? result.Message : $"{T("OperationFailed")} {result.Message}";
        return RedirectToPage(BuildRouteValues());
    }

    private string T(string key) => UiTextCatalog.Get(CurrentLanguage, key);

    private static string NormalizeStatusFilter(string? value)
        => value?.ToLowerInvariant() switch
        {
            "ok" => "ok",
            "fail" => "fail",
            "disabled" => "disabled",
            "blocked" => "blocked",
            _ => "all"
        };

    private static string NormalizeProtocolFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim().ToLowerInvariant();

    private static string NormalizeAuditDateFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string NormalizeAuditActionFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "all" : value.Trim();

    private static string NormalizeAuditResultFilter(string? value)
        => value?.ToLowerInvariant() switch
        {
            "ok" => "ok",
            "fail" => "fail",
            _ => "all"
        };

    private object BuildRouteValues()
        => new
        {
            statusFilter = NormalizeStatusFilter(StatusFilter),
            protocolFilter = NormalizeProtocolFilter(ProtocolFilter),
            auditDateFilter = NormalizeAuditDateFilter(AuditDateFilter),
            auditActionFilter = NormalizeAuditActionFilter(AuditActionFilter),
            auditResultFilter = NormalizeAuditResultFilter(AuditResultFilter)
        };
}
