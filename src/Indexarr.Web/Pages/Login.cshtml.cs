using System.Security.Claims;
using Indexarr.Web.Localization;
using Indexarr.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Indexarr.Web.Pages;

public sealed class LoginModel : PageModel
{
    private readonly AuthService _authService;
    private readonly AppConfigurationService _configurationService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(AuthService authService, AppConfigurationService configurationService, ILogger<LoginModel> logger)
    {
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;
    }

    [BindProperty]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool IsFirstRun { get; private set; }

    public string CurrentLanguage { get; private set; } = "it";

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(GetSafeReturnUrl());
        }

        await InitializeAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await InitializeAsync();

        if (IsFirstRun)
        {
            return await RegisterAsync();
        }

        return await LoginAsync();
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToPage("/Index");
    }

    private async Task<IActionResult> RegisterAsync()
    {
        if (Username.Trim().Length < 3)
        {
            ModelState.AddModelError(string.Empty, T("UsernameTooShort"));
            return Page();
        }

        if (Password.Length < 6)
        {
            ModelState.AddModelError(string.Empty, T("NewPasswordTooShort"));
            return Page();
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(string.Empty, T("PasswordsDoNotMatch"));
            return Page();
        }

        try
        {
            var user = await _authService.RegisterFirstUserAsync(Username, Password, HttpContext.RequestAborted);
            await SignInAsync(user.Id, user.Username, user.Role);
            return LocalRedirect(GetSafeReturnUrl());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "First-user registration failed.");
            ModelState.AddModelError(string.Empty, T("RegistrationFailed"));
            return Page();
        }
    }

    private async Task<IActionResult> LoginAsync()
    {
        var user = await _authService.ValidateCredentialsAsync(Username, Password, HttpContext.RequestAborted);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, T("InvalidCredentials"));
            return Page();
        }

        await SignInAsync(user.Id, user.Username, user.Role);
        return LocalRedirect(GetSafeReturnUrl());
    }

    private string T(string key) => UiTextCatalog.Get(CurrentLanguage, key);

    private async Task InitializeAsync()
    {
        IsFirstRun = !await _authService.HasUsersAsync(HttpContext.RequestAborted);
        var configuration = await _configurationService.GetAsync(HttpContext.RequestAborted);
        CurrentLanguage = UiTextCatalog.Normalize(configuration?.Language);
        ViewData["CurrentLanguage"] = CurrentLanguage;
    }

    private async Task SignInAsync(int userId, string username, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(24)
            });
    }

    private string GetSafeReturnUrl()
        => !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : Url.Page("/Settings") ?? "/Settings";
}
