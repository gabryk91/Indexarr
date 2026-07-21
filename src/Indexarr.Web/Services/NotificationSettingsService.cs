using Indexarr.Web.Data;
using Indexarr.Web.Data.Entities;
using Indexarr.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Indexarr.Web.Services;

public sealed class NotificationSettingsService
{
    private readonly IndexarrDbContext _dbContext;

    public NotificationSettingsService(IndexarrDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NotificationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.NotificationSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return entity is null ? new NotificationSettings() : ToModel(entity);
    }

    public async Task SaveAsync(NotificationSettings settings, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.NotificationSettings.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (entity is null)
        {
            entity = new NotificationSettingsEntity();
            _dbContext.NotificationSettings.Add(entity);
        }

        entity.TelegramEnabled = settings.TelegramEnabled;
        entity.TelegramBotToken = settings.TelegramBotToken?.Trim() ?? string.Empty;
        entity.TelegramChatId = settings.TelegramChatId?.Trim() ?? string.Empty;
        entity.PushoverEnabled = settings.PushoverEnabled;
        entity.PushoverUserKey = settings.PushoverUserKey?.Trim() ?? string.Empty;
        entity.PushoverApiToken = settings.PushoverApiToken?.Trim() ?? string.Empty;
        entity.GotifyEnabled = settings.GotifyEnabled;
        entity.GotifyServerUrl = settings.GotifyServerUrl?.Trim() ?? string.Empty;
        entity.GotifyAppToken = settings.GotifyAppToken?.Trim() ?? string.Empty;
        entity.NotifyIndexerAutoDisabled = settings.NotifyIndexerAutoDisabled;
        entity.NotifyIndexerAutoEnabled = settings.NotifyIndexerAutoEnabled;
        entity.NotifyIndexerAutoAdded = settings.NotifyIndexerAutoAdded;
        entity.NotifyProwlarrUnreachable = settings.NotifyProwlarrUnreachable;
        entity.NotifyBackupCreated = settings.NotifyBackupCreated;
        entity.NotifyRestoreCompleted = settings.NotifyRestoreCompleted;
        entity.NotifyRollbackError = settings.NotifyRollbackError;
        entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static NotificationSettings ToModel(NotificationSettingsEntity entity)
        => new()
        {
            TelegramEnabled = entity.TelegramEnabled,
            TelegramBotToken = entity.TelegramBotToken,
            TelegramChatId = entity.TelegramChatId,
            PushoverEnabled = entity.PushoverEnabled,
            PushoverUserKey = entity.PushoverUserKey,
            PushoverApiToken = entity.PushoverApiToken,
            GotifyEnabled = entity.GotifyEnabled,
            GotifyServerUrl = entity.GotifyServerUrl,
            GotifyAppToken = entity.GotifyAppToken,
            NotifyIndexerAutoDisabled = entity.NotifyIndexerAutoDisabled,
            NotifyIndexerAutoEnabled = entity.NotifyIndexerAutoEnabled,
            NotifyIndexerAutoAdded = entity.NotifyIndexerAutoAdded,
            NotifyProwlarrUnreachable = entity.NotifyProwlarrUnreachable,
            NotifyBackupCreated = entity.NotifyBackupCreated,
            NotifyRestoreCompleted = entity.NotifyRestoreCompleted,
            NotifyRollbackError = entity.NotifyRollbackError
        };
}
