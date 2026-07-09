using Indexarr.Web.Localization;
using Indexarr.Web.Models;
using Indexarr.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Indexarr.Web.Pages;

public sealed class SetupModel : PageModel
{
    private readonly AppConfigurationService _configurationService;
    private readonly ProwlarrConnectionService _connectionService;
    private readonly AuthService _authService;
    private readonly ILogger<SetupModel> _logger;

    public SetupModel(
        AppConfigurationService configurationService,
        ProwlarrConnectionService connectionService,
        AuthService authService,
        ILogger<SetupModel> logger)
    {
        _configurationService = configurationService;
        _connectionService = connectionService;
        _authService = authService;
        _logger = logger;
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
        _logger.LogInformation("Setup test connection requested. Step={Step} Url={Url}", CurrentStep, Input.ProwlarrUrl);

        if (await _authService.HasUsersAsync(HttpContext.RequestAborted) && User.Identity?.IsAuthenticated != true)
        {
            return RedirectToPage("/Login", new { returnUrl = Url.Page("/Setup") });
        }

        CurrentStep = 2;
        ViewData["CurrentLanguage"] = CurrentLanguage;

        ValidateConnectionFields();
        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Setup test connection validation failed: {Errors}", DescribeModelState());
            return Page();
        }

        var result = await _connectionService.TestAsync(Input.ProwlarrUrl, Input.ProwlarrApiKey, CurrentLanguage, HttpContext.RequestAborted);
        ConnectionTested = true;
        ConnectionSucceeded = result.Success;
        ConnectionMessage = result.Message;

        if (!result.Success)
        {
            _logger.LogWarning("Setup test connection failed: {Message}", result.Message);
            ModelState.AddModelError(string.Empty, result.Message);
        }
        else
        {
            _logger.LogInformation("Setup test connection succeeded.");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        _logger.LogInformation("Setup save requested. Step={Step} Url={Url} Mode={Mode}", CurrentStep, Input.ProwlarrUrl, Input.Mode);

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
            _logger.LogWarning("Setup save validation failed: {Errors}", DescribeModelState());
            return Page();
        }

        var result = await _connectionService.TestAsync(Input.ProwlarrUrl, Input.ProwlarrApiKey, CurrentLanguage, HttpContext.RequestAborted);
        ConnectionTested = true;
        ConnectionSucceeded = result.Success;
        ConnectionMessage = result.Message;

        if (!result.Success)
        {
            CurrentStep = 2;
            _logger.LogWarning("Setup save aborted because Prowlarr test failed: {Message}", result.Message);
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        await _configurationService.SaveAsync(Input, HttpContext.RequestAborted);
        _logger.LogInformation("Setup configuration saved successfully.");
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

    private string DescribeModelState()
        => string.Join(" | ",
            ModelState
                .Where(entry => entry.Value is { Errors.Count: > 0 })
                .SelectMany(entry => entry.Value!.Errors.Select(error => $"{entry.Key}: {error.ErrorMessage}")));
}
