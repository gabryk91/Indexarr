using Indexarr.Web.Services.Notifications;

namespace Indexarr.Web.Services;

public sealed class NotificationDispatchService
{
    private readonly NotificationSettingsService _settingsService;
    private readonly TelegramNotificationSender _telegramSender;
    private readonly PushoverNotificationSender _pushoverSender;
    private readonly GotifyNotificationSender _gotifySender;

    public NotificationDispatchService(
        NotificationSettingsService settingsService,
        TelegramNotificationSender telegramSender,
        PushoverNotificationSender pushoverSender,
        GotifyNotificationSender gotifySender)
    {
        _settingsService = settingsService;
        _telegramSender = telegramSender;
        _pushoverSender = pushoverSender;
        _gotifySender = gotifySender;
    }

    public async Task NotifyAsync(NotificationEvent notificationEvent, string message, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.GetAsync(cancellationToken);
        if (!IsEventEnabled(settings, notificationEvent))
        {
            return;
        }

        if (settings.TelegramEnabled)
        {
            await _telegramSender.SendAsync(settings.TelegramBotToken, settings.TelegramChatId, message, cancellationToken);
        }

        if (settings.PushoverEnabled)
        {
            await _pushoverSender.SendAsync(settings.PushoverUserKey, settings.PushoverApiToken, message, cancellationToken);
        }

        if (settings.GotifyEnabled)
        {
            await _gotifySender.SendAsync(settings.GotifyServerUrl, settings.GotifyAppToken, message, cancellationToken);
        }
    }

    private static bool IsEventEnabled(Models.NotificationSettings settings, NotificationEvent notificationEvent)
        => notificationEvent switch
        {
            NotificationEvent.IndexerAutoDisabled => settings.NotifyIndexerAutoDisabled,
            NotificationEvent.IndexerAutoAdded => settings.NotifyIndexerAutoAdded,
            NotificationEvent.ProwlarrUnreachable => settings.NotifyProwlarrUnreachable,
            NotificationEvent.BackupCreated => settings.NotifyBackupCreated,
            NotificationEvent.RestoreCompleted => settings.NotifyRestoreCompleted,
            NotificationEvent.RollbackError => settings.NotifyRollbackError,
            _ => false
        };
}
