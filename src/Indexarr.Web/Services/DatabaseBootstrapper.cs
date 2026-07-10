using Microsoft.EntityFrameworkCore;

namespace Indexarr.Web.Services;

public sealed class DatabaseBootstrapper
{
    private readonly Indexarr.Web.Data.IndexarrDbContext _dbContext;

    public DatabaseBootstrapper(Indexarr.Web.Data.IndexarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.EnsureCreatedAsync(cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS AdminUsers (
                Id INTEGER NOT NULL CONSTRAINT PK_AdminUsers PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL,
                PasswordHash TEXT NOT NULL,
                Role TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                LastLoginAtUtc TEXT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS IndexerHealthChecks (
                Id INTEGER NOT NULL CONSTRAINT PK_IndexerHealthChecks PRIMARY KEY AUTOINCREMENT,
                IndexerId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                Protocol TEXT NOT NULL,
                Implementation TEXT NOT NULL,
                Result TEXT NOT NULL,
                Error TEXT NOT NULL,
                LatencyMs INTEGER NOT NULL,
                CheckedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS IndexerStates (
                IndexerId INTEGER NOT NULL CONSTRAINT PK_IndexerStates PRIMARY KEY,
                Name TEXT NOT NULL,
                Protocol TEXT NOT NULL,
                Implementation TEXT NOT NULL,
                Enabled INTEGER NOT NULL,
                LastResult TEXT NOT NULL,
                LastError TEXT NOT NULL,
                LastLatencyMs INTEGER NOT NULL,
                ConsecutiveFailures INTEGER NOT NULL,
                LastCheckedAtUtc TEXT NULL,
                LastActionAtUtc TEXT NULL,
                IsBlocked INTEGER NOT NULL DEFAULT 0
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS BlockedIndexers (
                Id INTEGER NOT NULL CONSTRAINT PK_BlockedIndexers PRIMARY KEY AUTOINCREMENT,
                OriginalIndexerId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                DefinitionName TEXT NOT NULL,
                Protocol TEXT NOT NULL,
                Implementation TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                BlockedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS AutoAddFailedCandidates (
                Id INTEGER NOT NULL CONSTRAINT PK_AutoAddFailedCandidates PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                DefinitionName TEXT NOT NULL,
                LastError TEXT NOT NULL,
                FailureCount INTEGER NOT NULL DEFAULT 1,
                LastAttemptAtUtc TEXT NOT NULL,
                NextRetryAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id INTEGER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY AUTOINCREMENT,
                IndexerId INTEGER NULL,
                IndexerName TEXT NOT NULL,
                Action TEXT NOT NULL,
                Mode TEXT NOT NULL,
                Succeeded INTEGER NOT NULL,
                Details TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS NotificationSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_NotificationSettings PRIMARY KEY,
                TelegramEnabled INTEGER NOT NULL DEFAULT 0,
                TelegramBotToken TEXT NOT NULL DEFAULT '',
                TelegramChatId TEXT NOT NULL DEFAULT '',
                PushoverEnabled INTEGER NOT NULL DEFAULT 0,
                PushoverUserKey TEXT NOT NULL DEFAULT '',
                PushoverApiToken TEXT NOT NULL DEFAULT '',
                GotifyEnabled INTEGER NOT NULL DEFAULT 0,
                GotifyServerUrl TEXT NOT NULL DEFAULT '',
                GotifyAppToken TEXT NOT NULL DEFAULT '',
                NotifyIndexerAutoDisabled INTEGER NOT NULL DEFAULT 1,
                NotifyIndexerAutoAdded INTEGER NOT NULL DEFAULT 1,
                NotifyProwlarrUnreachable INTEGER NOT NULL DEFAULT 1,
                NotifyBackupCreated INTEGER NOT NULL DEFAULT 0,
                NotifyRestoreCompleted INTEGER NOT NULL DEFAULT 1,
                NotifyRollbackError INTEGER NOT NULL DEFAULT 1,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            cancellationToken);

        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_IndexerHealthChecks_IndexerId_CheckedAtUtc ON IndexerHealthChecks (IndexerId, CheckedAtUtc DESC);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AdminUsers_Username ON AdminUsers (Username);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_CreatedAtUtc ON AuditLogs (CreatedAtUtc DESC);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BlockedIndexers_OriginalIndexerId ON BlockedIndexers (OriginalIndexerId);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BlockedIndexers_Name ON BlockedIndexers (Name);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_BlockedIndexers_DefinitionName ON BlockedIndexers (DefinitionName);",
            cancellationToken);
        await _dbContext.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_AutoAddFailedCandidates_Name ON AutoAddFailedCandidates (Name);",
            cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "HealthCheckTimeoutSeconds", "INTEGER NOT NULL DEFAULT 20", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutomationEnabled", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutomationIntervalMinutes", "INTEGER NOT NULL DEFAULT 15", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddProtocolFilter", "TEXT NOT NULL DEFAULT 'torrent'", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddLanguageFilter", "TEXT NOT NULL DEFAULT 'it,en'", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddCategoryFilter", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddLanguageMatchMode", "TEXT NOT NULL DEFAULT 'any'", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddAllowUnknownLanguage", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddPrivacyFilter", "TEXT NOT NULL DEFAULT 'public'", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddGlobalUsername", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddGlobalPassword", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddFailureCooldownHours", "INTEGER NOT NULL DEFAULT 24", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultQueryLimit", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultGrabLimit", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultLimitsUnit", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultAppMinimumSeeders", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultSeedRatio", "REAL NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultSeedTime", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultPackSeedTime", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultPreferMagnetUrlMode", "TEXT NOT NULL DEFAULT 'inherit'", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultIndexerPriority", "INTEGER NULL", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultDownloadClient", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultFilterByUploader", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("AppConfigurations", "AutoAddDefaultTags", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("IndexerStates", "IsBlocked", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "TelegramEnabled", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "TelegramBotToken", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "TelegramChatId", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "PushoverEnabled", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "PushoverUserKey", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "PushoverApiToken", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "GotifyEnabled", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "GotifyServerUrl", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "GotifyAppToken", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyIndexerAutoDisabled", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyIndexerAutoAdded", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyProwlarrUnreachable", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyBackupCreated", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyRestoreCompleted", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "NotifyRollbackError", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnAsync("NotificationSettings", "UpdatedAtUtc", "TEXT NOT NULL DEFAULT ''", cancellationToken);
    }

    private async Task EnsureColumnAsync(string tableName, string columnName, string columnDefinition, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Database.SqlQueryRaw<int>(
            $"SELECT COUNT(1) AS Value FROM pragma_table_info('{tableName}') WHERE name = '{columnName}'")
            .SingleAsync(cancellationToken);

        if (exists == 0)
        {
            await _dbContext.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
                cancellationToken);
        }
    }
}
