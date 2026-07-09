using Indexarr.Web.Localization;
using Indexarr.Web.Models;
using Indexarr.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Indexarr.Web.Pages;

public sealed class SetupModel : PageModel
{
    private readonly AppConfigurationService _configurationService;
    private readonly ProwlarrConnectionService _connectionService;
    private readonly IndexerAutomationService _automationService;
    private readonly AuthService _authService;

    public SetupModel(
        AppConfigurationService configurationService,
        ProwlarrConnectionService connectionService,
        IndexerAutomationService automationService,
        AuthService authService)
    {
        _configurationService = configurationService;
        _connectionService = connectionService;
        _automationService = automationService;
        _authService = authService;
    }

    [BindProperty]
    public SetupDraft Input { get; set; } = new();

    [BindProperty]
    public int CurrentStep { get; set; } = 1;

    public bool SavedSuccessfully { get; private set; }

    public bool ConnectionTested { get; private set; }

    public bool ConnectionSucceeded { get; private set; }

    public string ConnectionMessage { get; private set; } = string.Empty;

    public string CurrentLanguage => UiTextCatalog.Normalize(Input.Language);

    public string? Reason { get; private set; }

    public async Task OnGetAsync(string? reason)
    {
        if (await _authService.HasUsersAsync(HttpContext.RequestAborted) && User.Identity?.IsAuthenticated != true)
        {
            Response.Redirect(Url.Page("/Login", new { returnUrl = Url.Page("/Setup", new { reason }) }) ?? "/Login");
            return;
        }

        var draft = await _configurationService.GetAsync(HttpContext.RequestAborted);
        if (draft is not null)
        {
            Input = draft;
        }

        Reason = reason;
        CurrentStep = reason == "prowlarr-unreachable" ? 2 : 1;
        ViewData["CurrentLanguage"] = CurrentLanguage;
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        if (await _authService.HasUsersAsync(HttpContext.RequestAborted) && User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Setup") });
        }

        CurrentStep = 2;
        ViewData["CurrentLanguage"] = CurrentLanguage;

        ValidateConnectionFields();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _connectionService.TestAsync(Input.ProwlarrUrl, Input.ProwlarrApiKey, HttpContext.RequestAborted);
        ConnectionTested = true;
        ConnectionSucceeded = result.Success;
        ConnectionMessage = result.Message;

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await _authService.HasUsersAsync(HttpContext.RequestAborted) && User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Setup") });
        }

        CurrentStep = Math.Clamp(CurrentStep, 1, 4);
        var current = await _configurationService.GetAsync(HttpContext.RequestAborted) ?? new SetupDraft();
        Input = MergeWizardDraft(current, Input);
        ModelState.Clear();
        ViewData["CurrentLanguage"] = CurrentLanguage;

        if (!TryValidateModel(Input, nameof(Input)))
        {
            return Page();
        }

        var result = await _connectionService.TestAsync(Input.ProwlarrUrl, Input.ProwlarrApiKey, HttpContext.RequestAborted);
        ConnectionTested = true;
        ConnectionSucceeded = result.Success;
        ConnectionMessage = result.Message;

        if (!result.Success)
        {
            CurrentStep = 2;
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await _configurationService.SaveAsync(Input, HttpContext.RequestAborted);
        await _automationService.RunHealthChecksAsync("setup-complete", HttpContext.RequestAborted);
        SavedSuccessfully = true;

        return RedirectToPage("/Index");
    }

    private void ValidateConnectionFields()
    {
        KeepOnly(nameof(Input.ProwlarrUrl), nameof(Input.ProwlarrApiKey), nameof(Input.Language), nameof(Input.Timezone));
        TryValidateModel(Input, nameof(Input));
    }

    private void KeepOnly(params string[] names)
    {
        var allowed = names.Select(name => $"Input.{name}").ToHashSet(StringComparer.Ordinal);
        foreach (var key in ModelState.Keys.ToList())
        {
            if (!allowed.Contains(key))
            {
                ModelState.Remove(key);
            }
        }
    }

    private static SetupDraft MergeWizardDraft(SetupDraft current, SetupDraft posted)
        => new()
        {
            Language = posted.Language,
            Timezone = posted.Timezone,
            Mode = posted.Mode,
            FailureThreshold = posted.FailureThreshold,
            HealthCheckTimeoutSeconds = current.HealthCheckTimeoutSeconds,
            AutomationEnabled = current.AutomationEnabled,
            AutomationIntervalMinutes = current.AutomationIntervalMinutes,
            BackupBeforeChanges = posted.BackupBeforeChanges,
            AutoDisableFailedIndexers = posted.AutoDisableFailedIndexers,
            AutoAddPublicIndexers = posted.AutoAddPublicIndexers,
            AutoAddProtocolFilter = current.AutoAddProtocolFilter,
            AutoAddLanguageFilter = current.AutoAddLanguageFilter,
            AutoAddCategoryFilter = current.AutoAddCategoryFilter,
            AutoAddLanguageMatchMode = current.AutoAddLanguageMatchMode,
            AutoAddAllowUnknownLanguage = current.AutoAddAllowUnknownLanguage,
            AutoAddPrivacyFilter = current.AutoAddPrivacyFilter,
            AutoAddGlobalUsername = current.AutoAddGlobalUsername,
            AutoAddGlobalPassword = current.AutoAddGlobalPassword,
            AutoAddFailureCooldownHours = current.AutoAddFailureCooldownHours,
            AutoAddDefaultQueryLimit = current.AutoAddDefaultQueryLimit,
            AutoAddDefaultGrabLimit = current.AutoAddDefaultGrabLimit,
            AutoAddDefaultLimitsUnit = current.AutoAddDefaultLimitsUnit,
            AutoAddDefaultAppMinimumSeeders = current.AutoAddDefaultAppMinimumSeeders,
            AutoAddDefaultSeedRatio = current.AutoAddDefaultSeedRatio,
            AutoAddDefaultSeedTime = current.AutoAddDefaultSeedTime,
            AutoAddDefaultPackSeedTime = current.AutoAddDefaultPackSeedTime,
            AutoAddDefaultPreferMagnetUrlMode = current.AutoAddDefaultPreferMagnetUrlMode,
            AutoAddDefaultIndexerPriority = current.AutoAddDefaultIndexerPriority,
            AutoAddDefaultDownloadClient = current.AutoAddDefaultDownloadClient,
            AutoAddDefaultFilterByUploader = current.AutoAddDefaultFilterByUploader,
            AutoAddDefaultTags = current.AutoAddDefaultTags,
            ProwlarrUrl = posted.ProwlarrUrl,
            ProwlarrApiKey = posted.ProwlarrApiKey
        };
}
