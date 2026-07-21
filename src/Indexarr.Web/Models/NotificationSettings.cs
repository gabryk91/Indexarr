namespace Indexarr.Web.Models;

public sealed class NotificationSettings
{
    public bool TelegramEnabled { get; set; }

    public string TelegramBotToken { get; set; } = string.Empty;

    public string TelegramChatId { get; set; } = string.Empty;

    public bool PushoverEnabled { get; set; }

    public string PushoverUserKey { get; set; } = string.Empty;

    public string PushoverApiToken { get; set; } = string.Empty;

    public bool GotifyEnabled { get; set; }

    public string GotifyServerUrl { get; set; } = string.Empty;

    public string GotifyAppToken { get; set; } = string.Empty;

    public bool NotifyIndexerAutoDisabled { get; set; } = true;

    public bool NotifyIndexerAutoEnabled { get; set; } = true;

    public bool NotifyIndexerAutoAdded { get; set; } = true;

    public bool NotifyProwlarrUnreachable { get; set; } = true;

    public bool NotifyBackupCreated { get; set; }

    public bool NotifyRestoreCompleted { get; set; } = true;

    public bool NotifyRollbackError { get; set; } = true;
}
