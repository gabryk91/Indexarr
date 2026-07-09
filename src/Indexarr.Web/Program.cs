using Indexarr.Web.Data;
using Indexarr.Web.Options;
using Indexarr.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var configDirectory = ResolveStorageDirectory(
    builder.Configuration[$"{IndexarrOptions.SectionName}:{nameof(IndexarrOptions.ConfigPath)}"] ?? "/config",
    builder.Environment.ContentRootPath);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(configDirectory, "keys")))
    .SetApplicationName("Indexarr");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.Cookie.Name = "Indexarr.Auth";
    });
builder.Services.AddAuthorization();
builder.Services.AddOptions<IndexarrOptions>()
    .Bind(builder.Configuration.GetSection(IndexarrOptions.SectionName));
builder.Services.AddSingleton<AutomationRuntimeState>();
builder.Services.AddSingleton<StoragePathResolver>();
builder.Services.AddSingleton<SetupDraftStore>();
builder.Services.AddHttpClient(nameof(ProwlarrConnectionService));
builder.Services.AddHttpClient(nameof(ProwlarrApiClient));
builder.Services.AddScoped<DatabaseBootstrapper>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ProwlarrConnectionService>();
builder.Services.AddScoped<ProwlarrApiClient>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<IndexerBackupService>();
builder.Services.AddScoped<IndexerAutomationService>();
builder.Services.AddScoped<ProwlarrDashboardService>();
builder.Services.AddScoped<AppConfigurationService>();
builder.Services.AddHostedService<IndexerAutomationHostedService>();
builder.Services.AddDbContext<IndexarrDbContext>((serviceProvider, options) =>
{
    var indexarrOptions = serviceProvider.GetRequiredService<IOptions<IndexarrOptions>>().Value;
    var pathResolver = serviceProvider.GetRequiredService<StoragePathResolver>();
    var databasePath = pathResolver.ResolveFilePath(indexarrOptions.ConfigPath, "Indexarr.db");
    options.UseSqlite($"Data Source={databasePath}");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var bootstrapper = scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>();
    await bootstrapper.InitializeAsync();

    var configurationService = scope.ServiceProvider.GetRequiredService<AppConfigurationService>();
    var setupDraftStore = scope.ServiceProvider.GetRequiredService<SetupDraftStore>();
    await configurationService.EnsureMigratedFromDraftAsync(setupDraftStore);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseRouting();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    try
    {
        var path = context.Request.Path;
        if (Path.HasExtension(path)
            || path.StartsWithSegments("/Setup", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/readyz", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/meta", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/automation-status", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Error", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        using var scope = app.Services.CreateScope();
        var configurationService = scope.ServiceProvider.GetRequiredService<AppConfigurationService>();
        var connectionService = scope.ServiceProvider.GetRequiredService<ProwlarrConnectionService>();
        var draft = await configurationService.GetAsync(context.RequestAborted);

        if (draft is null)
        {
            context.Response.Redirect("/Setup?reason=first-run");
            return;
        }

        var connection = await connectionService.TestAsync(draft.ProwlarrUrl, draft.ProwlarrApiKey, draft.Language, context.RequestAborted);
        if (!connection.Success && context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Redirect("/Setup?reason=prowlarr-unreachable");
            return;
        }

        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return;
    }
});

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();
app.MapGet("/healthz", async (IndexarrDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ok", database = "reachable" })
        : Results.Json(new { status = "degraded", database = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/readyz", async (AppConfigurationService configurationService, ProwlarrConnectionService connectionService, CancellationToken cancellationToken) =>
{
    var configuration = await configurationService.GetAsync(cancellationToken);
    if (configuration is null)
    {
        return Results.Json(new { status = "not-ready", reason = "configuration-missing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var result = await connectionService.TestAsync(configuration.ProwlarrUrl, configuration.ProwlarrApiKey, configuration.Language, cancellationToken);
    return result.Success
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not-ready", reason = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/api/meta", async (IOptions<IndexarrOptions> options, AppConfigurationService configurationService, CancellationToken cancellationToken) =>
{
    var value = options.Value;
    var configuration = await configurationService.GetAsync(cancellationToken);

    return Results.Ok(new
    {
        product = value.ProductName,
        mode = configuration?.Mode ?? value.Mode,
        storage = new
        {
            config = value.ConfigPath,
            backups = value.BackupPath,
            logs = value.LogsPath
        },
        automation = new
        {
            enabled = configuration?.AutomationEnabled ?? value.Automation.Enabled,
            intervalMinutes = configuration?.AutomationIntervalMinutes ?? value.Automation.IntervalMinutes
        },
        prowlarrUrl = configuration?.ProwlarrUrl ?? value.Prowlarr.Url
    });
});

app.MapGet("/api/automation-status", (AutomationRuntimeState runtimeState) =>
{
    return Results.Ok(new
    {
        enabled = runtimeState.IsEnabled,
        running = runtimeState.IsRunning,
        intervalSeconds = Math.Max(1, runtimeState.IntervalMinutes) * 60,
        nextRunUtc = runtimeState.NextScheduledAtUtc?.ToString("O"),
        lastStartedUtc = runtimeState.LastStartedAtUtc?.ToString("O"),
        lastCompletedUtc = runtimeState.LastCompletedAtUtc?.ToString("O"),
        lastSucceeded = runtimeState.LastSucceeded,
        lastMessage = runtimeState.LastMessage
    });
});

app.Run();

static string ResolveStorageDirectory(string configuredPath, string contentRootPath)
{
    if (OperatingSystem.IsWindows() && configuredPath.StartsWith('/'))
    {
        return Path.Combine(contentRootPath, configuredPath.Trim('/').Replace('/', Path.DirectorySeparatorChar));
    }

    if (Path.IsPathRooted(configuredPath))
    {
        return configuredPath;
    }

    return Path.Combine(contentRootPath, configuredPath);
}
