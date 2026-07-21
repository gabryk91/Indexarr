namespace Indexarr.Web.Services.Notifications;

public enum NotificationEvent
{
    IndexerAutoDisabled,
    IndexerAutoEnabled,
    IndexerAutoAdded,
    ProwlarrUnreachable,
    BackupCreated,
    RestoreCompleted,
    RollbackError
}
