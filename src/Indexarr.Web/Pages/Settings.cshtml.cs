using System.Security.Claims;
using System.Text.Json;
using Indexarr.Web.Localization;
using Indexarr.Web.Models;
using Indexarr.Web.Services;
using Indexarr.Web.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Indexarr.Web.Pages;

[Authorize]
public sealed class SettingsModel : PageModel
{
    private readonly AppConfigurationService _configurationService;
    private readonly ProwlarrConnectionService _connectionService;
    private readonly AuthService _authService;
    private readonly ProwlarrApiClient _apiClient;
    private readonly IndexerBackupService _backupService;
    private readonly IndexerAutomationService _automationService;
    private readonly NotificationSettingsService _notificationSettingsService;
    private readonly NotificationDispatchService _notificationDispatchService;
    private readonly TelegramNotificationSender _telegramSender;
    private readonly PushoverNotificationSender _pushoverSender;
    private readonly GotifyNotificationSender _gotifySender;

    public SettingsModel(
        AppConfigurationService configurationService,
        ProwlarrConnectionService connectionService,
        AuthService authService,
        ProwlarrApiClient apiClient,
        IndexerBackupService backupService,
        IndexerAutomationService automationService,
        NotificationSettingsService notificationSettingsService,
        NotificationDispatchService notificationDispatchService,
        TelegramNotificationSender telegramSender,
        PushoverNotificationSender pushoverSender,
        GotifyNotificationSender gotifySender)
    {
        _configurationService = configurationService;
        _connectionService = connectionService;
        _authService = authService;
        _apiClient = apiClient;
        _backupService = backupService;
        _automationService = automationService;
        _notificationSettingsService = notificationSettingsService;
        _notificationDispatchService = notificationDispatchService;
        _telegramSender = telegramSender;
        _pushoverSender = pushoverSender;
        _gotifySender = gotifySender;
    }

    [BindProperty]
    public SetupDraft Input { get; set; } = new();

    [BindProperty]
    public NotificationSettings NotificationsInput { get; set; } = new();

    [BindProperty]
    public string? CurrentPassword { get; set; }

    [BindProperty]
    public string? NewPassword { get; set; }

    [BindProperty]
    public string? ConfirmNewPassword { get; set; }

    [BindProperty]
    public List<int> SelectedAutoAddCategoryIds { get; set; } = [];

    [BindProperty]
    public List<string> SelectedAutoAddPrivacyFilters { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Tab { get; set; } = "general";

    [TempData]
    public string? FlashMessage { get; set; }

    public IReadOnlyList<ProwlarrDownloadClientOption> DownloadClients { get; private set; } = [];

    public IReadOnlyList<ProwlarrCategoryOption> AvailableCategories { get; private set; } = [];

    public IReadOnlyList<ProwlarrTagOption> AvailableTags { get; private set; } = [];

    public int AutoAddCooldownCount { get; private set; }

    public string AvailableTagsJson => JsonSerializer.Serialize(AvailableTags);

    public string CurrentLanguage => UiTextCatalog.Normalize(Input.Language);

    public string CurrentUsername => User.Identity?.Name ?? "admin";

    public async Task<IActionResult> OnGetAsync()
    {
        var configuration = await _configurationService.GetAsync(HttpContext.RequestAborted);
        if (configuration is null)
        {
            return RedirectToPage("/Setup", new { reason = "first-run" });
        }

        Input = configuration;
        Tab = NormalizeTab(Tab);
        ViewData["CurrentLanguage"] = CurrentLanguage;
        if (RequiresSelectionData(Tab))
        {
            await LoadSelectionDataAsync(configuration);
        }
        else
        {
            ResetSelectionData(configuration);
        }

        NotificationsInput = await _notificationSettingsService.GetAsync(HttpContext.RequestAborted);

        AutoAddCooldownCount = await _automationService.GetAutoAddCooldownCountAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = NormalizeTab(Tab);
        NormalizePostedSelections(Tab);
        var merged = MergeDraft((SetupDraft)configuration, Input, Tab);
        if (string.Equals(Tab, "protections", StringComparison.OrdinalIgnoreCase))
        {
            // Le checkbox di categorie/privacy esistono solo nel form del tab "Protezioni":
            // vanno applicate solo quando è quel tab a essere stato salvato, altrimenti
            // negli altri tab (dove queste checkbox non vengono postate) finiremmo per
            // svuotare categorie e filtro privacy già salvati.
            merged.AutoAddCategoryFilter = string.Join(",", SelectedAutoAddCategoryIds.Distinct().OrderBy(x => x));
            merged.AutoAddPrivacyFilter = SerializePrivacyFilters(SelectedAutoAddPrivacyFilters);
        }
        Input = merged;
        if (RequiresSelectionData(Tab))
        {
            await LoadSelectionDataAsync(Input);
        }
        else
        {
            ResetSelectionData(Input);
        }

        AutoAddCooldownCount = await _automationService.GetAutoAddCooldownCountAsync(HttpContext.RequestAborted);
        if (string.Equals(Tab, "protections", StringComparison.OrdinalIgnoreCase))
        {
            Input.AutoAddDefaultTags = SanitizeDefaultTags(Input.AutoAddDefaultTags, AvailableTags);
        }

        ModelState.Clear();
        if (!TryValidateModel(Input, nameof(Input)))
        {
            ViewData["CurrentLanguage"] = CurrentLanguage;
            return Page();
        }

        var current = (SetupDraft)configuration;
        var isEnablingAutomation = !current.AutomationEnabled && Input.AutomationEnabled;
        if (isEnablingAutomation)
        {
            var blockers = await ValidateAutomationActivationAsync(Input);
            if (blockers.Count > 0)
            {
                ModelState.AddModelError(string.Empty, $"{T("AutomationSaveBlockedTitle")} {string.Join("; ", blockers)}.");
            }
        }

        var warnings = BuildAutomationWarnings(Input);
        if (!ModelState.IsValid)
        {
            ViewData["CurrentLanguage"] = CurrentLanguage;
            return Page();
        }

        await _configurationService.SaveAsync(Input, HttpContext.RequestAborted);
        FlashMessage = warnings.Count == 0
            ? T("SettingsSaved")
            : $"{T("SettingsSaved")} {string.Join(" ", warnings)}";
        return RedirectToPage(new { tab = Tab });
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Input = MergeDraft((SetupDraft)configuration, Input, "integrations");
        ModelState.Clear();
        ViewData["CurrentLanguage"] = CurrentLanguage;
        Tab = "integrations";
        ResetSelectionData(Input);
        var result = await _connectionService.TestAsync(Input.ProwlarrUrl, Input.ProwlarrApiKey, CurrentLanguage, HttpContext.RequestAborted);
        FlashMessage = result.Success ? T("ConnectionOk") : $"{T("ConnectionFailedWithDetails")} {result.Message}";
        return Page();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        ViewData["CurrentLanguage"] = CurrentLanguage;
        Tab = "account";

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            ModelState.AddModelError(string.Empty, T("NewPasswordTooShort"));
            ResetSelectionData((SetupDraft)configuration);
            return Page();
        }

        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, T("PasswordsDoNotMatch"));
            ResetSelectionData((SetupDraft)configuration);
            return Page();
        }

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Challenge();
        }

        var success = await _authService.ChangePasswordAsync(userId, CurrentPassword ?? string.Empty, NewPassword, HttpContext.RequestAborted);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, T("CurrentPasswordInvalid"));
            ResetSelectionData((SetupDraft)configuration);
            return Page();
        }

        FlashMessage = T("PasswordUpdated");
        return RedirectToPage(new { tab = "account" });
    }

    public async Task<IActionResult> OnPostBackupNowAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "maintenance";
        ViewData["CurrentLanguage"] = CurrentLanguage;
        Input = (SetupDraft)configuration;
        var raw = await _apiClient.GetIndexersPayloadAsync(Input, HttpContext.RequestAborted);
        var path = await _backupService.SaveAsync("manual-backup", raw, HttpContext.RequestAborted);
        await _notificationDispatchService.NotifyAsync(
            NotificationEvent.BackupCreated,
            string.Format(T("NotifyMessageBackupCreated"), path),
            HttpContext.RequestAborted);
        FlashMessage = $"{T("BackupCreated")} {path}";
        return RedirectToPage(new { tab = "maintenance" });
    }

    public async Task<IActionResult> OnPostSaveNotificationsAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "notifications";
        Input = (SetupDraft)configuration;
        ViewData["CurrentLanguage"] = CurrentLanguage;
        await _notificationSettingsService.SaveAsync(NotificationsInput, HttpContext.RequestAborted);
        FlashMessage = T("SettingsSaved");
        return RedirectToPage(new { tab = "notifications" });
    }

    public async Task<IActionResult> OnPostTestTelegramAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "notifications";
        Input = (SetupDraft)configuration;
        ViewData["CurrentLanguage"] = CurrentLanguage;
        var result = await _telegramSender.SendAsync(
            NotificationsInput.TelegramBotToken,
            NotificationsInput.TelegramChatId,
            T("NotifyTestMessage"),
            HttpContext.RequestAborted);
        FlashMessage = result.Success ? T("NotificationTestOk") : $"{T("NotificationTestFailed")} {result.Message}";
        return Page();
    }

    public async Task<IActionResult> OnPostTestPushoverAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "notifications";
        Input = (SetupDraft)configuration;
        ViewData["CurrentLanguage"] = CurrentLanguage;
        var result = await _pushoverSender.SendAsync(
            NotificationsInput.PushoverUserKey,
            NotificationsInput.PushoverApiToken,
            T("NotifyTestMessage"),
            HttpContext.RequestAborted);
        FlashMessage = result.Success ? T("NotificationTestOk") : $"{T("NotificationTestFailed")} {result.Message}";
        return Page();
    }

    public async Task<IActionResult> OnPostTestGotifyAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "notifications";
        Input = (SetupDraft)configuration;
        ViewData["CurrentLanguage"] = CurrentLanguage;
        var result = await _gotifySender.SendAsync(
            NotificationsInput.GotifyServerUrl,
            NotificationsInput.GotifyAppToken,
            T("NotifyTestMessage"),
            HttpContext.RequestAborted);
        FlashMessage = result.Success ? T("NotificationTestOk") : $"{T("NotificationTestFailed")} {result.Message}";
        return Page();
    }

    public async Task<IActionResult> OnPostClearAutoAddCooldownAsync()
    {
        var configuration = await EnsureConfigurationAsync();
        if (configuration is IActionResult actionResult)
        {
            return actionResult;
        }

        Tab = "maintenance";
        Input = (SetupDraft)configuration;
        await _automationService.ClearAutoAddCooldownAsync(HttpContext.RequestAborted);
        FlashMessage = T("AutoAddCooldownCleared");
        return RedirectToPage(new { tab = "maintenance" });
    }

    private async Task<object> EnsureConfigurationAsync()
    {
        var configuration = await _configurationService.GetAsync(HttpContext.RequestAborted);
        if (configuration is null)
        {
            return RedirectToPage("/Setup", new { reason = "first-run" });
        }

        return configuration;
    }

    private async Task LoadSelectionDataAsync(SetupDraft configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.ProwlarrUrl) || string.IsNullOrWhiteSpace(configuration.ProwlarrApiKey))
        {
            ResetSelectionData(configuration);
            return;
        }

        try
        {
            DownloadClients = await _apiClient.GetDownloadClientsAsync(configuration, HttpContext.RequestAborted);
        }
        catch
        {
            DownloadClients = [];
        }

        try
        {
            AvailableCategories = await _apiClient.GetIndexerCategoriesAsync(configuration, HttpContext.RequestAborted);
        }
        catch
        {
            AvailableCategories = [];
        }

        try
        {
            AvailableTags = await _apiClient.GetTagsAsync(configuration, HttpContext.RequestAborted);
        }
        catch
        {
            AvailableTags = [];
        }

        SelectedAutoAddCategoryIds = ParseCategoryIds(configuration.AutoAddCategoryFilter);
        SelectedAutoAddPrivacyFilters = ParsePrivacyFilters(configuration.AutoAddPrivacyFilter);
    }

    private void ResetSelectionData(SetupDraft configuration)
    {
        DownloadClients = [];
        AvailableCategories = [];
        AvailableTags = [];
        SelectedAutoAddCategoryIds = ParseCategoryIds(configuration.AutoAddCategoryFilter);
        SelectedAutoAddPrivacyFilters = ParsePrivacyFilters(configuration.AutoAddPrivacyFilter);
    }

    private static bool RequiresSelectionData(string? tab)
        => string.Equals(tab, "protections", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTab(string? tab)
        => tab?.ToLowerInvariant() switch
        {
            "general" => "general",
            "integrations" => "integrations",
            "protections" => "protections",
            "notifications" => "notifications",
            "account" => "account",
            "maintenance" => "maintenance",
            _ => "general"
        };

    private void NormalizePostedSelections(string? tab)
    {
        if (string.Equals(tab, "general", StringComparison.OrdinalIgnoreCase))
        {
            Input.AutomationEnabled = Request.Form["Input.AutomationEnabled"].Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

            if (int.TryParse(Request.Form["Input.AutomationIntervalMinutes"], out var intervalMinutes))
            {
                Input.AutomationIntervalMinutes = intervalMinutes;
            }
        }

        if (string.Equals(tab, "protections", StringComparison.OrdinalIgnoreCase))
        {
            SelectedAutoAddCategoryIds = Request.Form["SelectedAutoAddCategoryIds"]
                .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            SelectedAutoAddPrivacyFilters = Request.Form["SelectedAutoAddPrivacyFilters"]
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static SetupDraft MergeDraft(SetupDraft current, SetupDraft posted, string tab)
        => tab switch
        {
            "general" => new SetupDraft
            {
                Language = posted.Language,
                Timezone = posted.Timezone,
                Mode = posted.Mode,
                FailureThreshold = posted.FailureThreshold,
                HealthCheckTimeoutSeconds = posted.HealthCheckTimeoutSeconds,
                AutomationEnabled = posted.AutomationEnabled,
                AutomationIntervalMinutes = posted.AutomationIntervalMinutes,
                BackupBeforeChanges = current.BackupBeforeChanges,
                AutoDisableFailedIndexers = current.AutoDisableFailedIndexers,
                AutoAddPublicIndexers = current.AutoAddPublicIndexers,
                AutoAddProtocolFilter = current.AutoAddProtocolFilter,
                AutoAddLanguageFilter = current.AutoAddLanguageFilter,
                AutoAddCategoryFilter = current.AutoAddCategoryFilter,
                AutoAddLanguageMatchMode = current.AutoAddLanguageMatchMode,
                AutoAddAllowUnknownLanguage = current.AutoAddAllowUnknownLanguage,
                AutoAddPrivacyFilter = current.AutoAddPrivacyFilter,
                AutoAddGlobalUsername = current.AutoAddGlobalUsername,
                AutoAddGlobalPassword = current.AutoAddGlobalPassword,
                AutoAddFailureCooldownHours = posted.AutoAddFailureCooldownHours <= 0 ? current.AutoAddFailureCooldownHours : posted.AutoAddFailureCooldownHours,
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
                ProwlarrUrl = current.ProwlarrUrl,
                ProwlarrApiKey = current.ProwlarrApiKey
            },
            "maintenance" => new SetupDraft
            {
                Language = current.Language,
                Timezone = current.Timezone,
                Mode = current.Mode,
                FailureThreshold = current.FailureThreshold,
                HealthCheckTimeoutSeconds = current.HealthCheckTimeoutSeconds,
                AutomationEnabled = current.AutomationEnabled,
                AutomationIntervalMinutes = current.AutomationIntervalMinutes,
                BackupBeforeChanges = current.BackupBeforeChanges,
                AutoDisableFailedIndexers = current.AutoDisableFailedIndexers,
                AutoAddPublicIndexers = current.AutoAddPublicIndexers,
                AutoAddProtocolFilter = current.AutoAddProtocolFilter,
                AutoAddLanguageFilter = current.AutoAddLanguageFilter,
                AutoAddCategoryFilter = current.AutoAddCategoryFilter,
                AutoAddLanguageMatchMode = current.AutoAddLanguageMatchMode,
                AutoAddAllowUnknownLanguage = current.AutoAddAllowUnknownLanguage,
                AutoAddPrivacyFilter = current.AutoAddPrivacyFilter,
                AutoAddGlobalUsername = current.AutoAddGlobalUsername,
                AutoAddGlobalPassword = current.AutoAddGlobalPassword,
                AutoAddFailureCooldownHours = posted.AutoAddFailureCooldownHours <= 0 ? current.AutoAddFailureCooldownHours : posted.AutoAddFailureCooldownHours,
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
                ProwlarrUrl = current.ProwlarrUrl,
                ProwlarrApiKey = current.ProwlarrApiKey
            },
            "integrations" => new SetupDraft
            {
                Language = current.Language,
                Timezone = current.Timezone,
                Mode = current.Mode,
                FailureThreshold = current.FailureThreshold,
                HealthCheckTimeoutSeconds = current.HealthCheckTimeoutSeconds,
                AutomationEnabled = current.AutomationEnabled,
                AutomationIntervalMinutes = current.AutomationIntervalMinutes,
                BackupBeforeChanges = current.BackupBeforeChanges,
                AutoDisableFailedIndexers = current.AutoDisableFailedIndexers,
                AutoAddPublicIndexers = current.AutoAddPublicIndexers,
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
            },
            "protections" => new SetupDraft
            {
                Language = current.Language,
                Timezone = current.Timezone,
                Mode = current.Mode,
                FailureThreshold = current.FailureThreshold,
                HealthCheckTimeoutSeconds = current.HealthCheckTimeoutSeconds,
                AutomationEnabled = current.AutomationEnabled,
                AutomationIntervalMinutes = current.AutomationIntervalMinutes,
                BackupBeforeChanges = posted.BackupBeforeChanges,
                AutoDisableFailedIndexers = posted.AutoDisableFailedIndexers,
                AutoAddPublicIndexers = posted.AutoAddPublicIndexers,
                AutoAddProtocolFilter = posted.AutoAddProtocolFilter,
                AutoAddLanguageFilter = posted.AutoAddLanguageFilter,
                AutoAddCategoryFilter = posted.AutoAddCategoryFilter,
                AutoAddLanguageMatchMode = posted.AutoAddLanguageMatchMode,
                AutoAddAllowUnknownLanguage = posted.AutoAddAllowUnknownLanguage,
                AutoAddPrivacyFilter = posted.AutoAddPrivacyFilter,
                // I campi <input type="password"> non vengono mai ripopolati da ASP.NET Core
                // (per sicurezza), e se la checkbox "semi-private" è deselezionata il JS li
                // disabilita e non vengono nemmeno postati: in entrambi i casi il valore
                // arriverebbe vuoto. Se non è stato digitato nulla di nuovo, manteniamo il
                // valore già salvato invece di sovrascriverlo con una stringa vuota.
                AutoAddGlobalUsername = string.IsNullOrEmpty(posted.AutoAddGlobalUsername) ? current.AutoAddGlobalUsername : posted.AutoAddGlobalUsername,
                AutoAddGlobalPassword = string.IsNullOrEmpty(posted.AutoAddGlobalPassword) ? current.AutoAddGlobalPassword : posted.AutoAddGlobalPassword,
                AutoAddFailureCooldownHours = current.AutoAddFailureCooldownHours,
                AutoAddDefaultQueryLimit = posted.AutoAddDefaultQueryLimit,
                AutoAddDefaultGrabLimit = posted.AutoAddDefaultGrabLimit,
                AutoAddDefaultLimitsUnit = posted.AutoAddDefaultLimitsUnit,
                AutoAddDefaultAppMinimumSeeders = posted.AutoAddDefaultAppMinimumSeeders,
                AutoAddDefaultSeedRatio = posted.AutoAddDefaultSeedRatio,
                AutoAddDefaultSeedTime = posted.AutoAddDefaultSeedTime,
                AutoAddDefaultPackSeedTime = posted.AutoAddDefaultPackSeedTime,
                AutoAddDefaultPreferMagnetUrlMode = posted.AutoAddDefaultPreferMagnetUrlMode,
                AutoAddDefaultIndexerPriority = posted.AutoAddDefaultIndexerPriority,
                AutoAddDefaultDownloadClient = posted.AutoAddDefaultDownloadClient,
                AutoAddDefaultFilterByUploader = posted.AutoAddDefaultFilterByUploader,
                AutoAddDefaultTags = posted.AutoAddDefaultTags,
                ProwlarrUrl = current.ProwlarrUrl,
                ProwlarrApiKey = current.ProwlarrApiKey
            },
            _ => current
        };

    private static List<int> ParseCategoryIds(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => int.TryParse(x, out var parsed) ? parsed : 0)
                .Where(x => x > 0)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

    private static List<string> ParsePrivacyFilters(string? value)
    {
        var available = GetAvailablePrivacyFilters();
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return available;
        }

        var selected = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => available.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return selected.Count == 0 ? available : selected;
    }

    private static string SerializePrivacyFilters(IEnumerable<string>? values)
    {
        var available = GetAvailablePrivacyFilters();
        var selected = (values ?? [])
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => available.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => available.IndexOf(x))
            .ToList();

        return selected.Count == 0 || selected.Count == available.Count
            ? "all"
            : string.Join(",", selected);
    }

    private static List<string> GetAvailablePrivacyFilters()
        => ["public", "semi-private", "private"];

    private static string SanitizeDefaultTags(string? configuredTags, IReadOnlyList<ProwlarrTagOption> availableTags)
    {
        if (string.IsNullOrWhiteSpace(configuredTags) || availableTags.Count == 0)
        {
            return string.Empty;
        }

        var selected = configuredTags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => availableTags.Any(tag => string.Equals(tag.Label, x, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return selected.Count == 0 ? string.Empty : string.Join(", ", selected);
    }

    private async Task<List<string>> ValidateAutomationActivationAsync(SetupDraft configuration)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration.ProwlarrUrl))
        {
            issues.Add(T("AutomationValidationMissingProwlarrUrl"));
        }

        if (string.IsNullOrWhiteSpace(configuration.ProwlarrApiKey))
        {
            issues.Add(T("AutomationValidationMissingProwlarrApiKey"));
        }

        if (issues.Count > 0)
        {
            return issues;
        }

        var result = await _connectionService.TestAsync(configuration.ProwlarrUrl, configuration.ProwlarrApiKey, CurrentLanguage, HttpContext.RequestAborted);
        if (!result.Success)
        {
            issues.Add(T("AutomationValidationConnectionFailed"));
        }

        return issues;
    }

    private List<string> BuildAutomationWarnings(SetupDraft configuration)
    {
        var warnings = new List<string>();

        if (!configuration.AutoAddPublicIndexers)
        {
            return warnings;
        }

        if (string.Equals(Tab, "protections", StringComparison.OrdinalIgnoreCase) && AvailableCategories.Count == 0)
        {
            warnings.Add(T("AutomationWarningAutoInsertCategoriesUnavailable"));
        }
        else if (ParseCategoryIds(configuration.AutoAddCategoryFilter).Count == 0)
        {
            warnings.Add(T("AutomationWarningAutoInsertNoCategories"));
        }

        return warnings;
    }

    private string T(string key) => UiTextCatalog.Get(CurrentLanguage, key);
}
