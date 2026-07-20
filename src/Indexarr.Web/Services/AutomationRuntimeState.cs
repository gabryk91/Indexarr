namespace Indexarr.Web.Services;

public sealed class AutomationRuntimeState
{
    private readonly object _sync = new();

    public bool IsEnabled { get; private set; } = true;

    public int IntervalMinutes { get; private set; } = 60;

    public bool IsRunning { get; private set; }

    public string CurrentTrigger { get; private set; } = "idle";

    public DateTimeOffset? LastStartedAtUtc { get; private set; }

    public DateTimeOffset? LastCompletedAtUtc { get; private set; }

    public bool? LastSucceeded { get; private set; }

    public string LastMessage { get; private set; } = "No runs yet.";

    public DateTimeOffset? NextScheduledAtUtc { get; private set; }

    public void Configure(bool enabled, int intervalMinutes)
    {
        lock (_sync)
        {
            IsEnabled = enabled;
            IntervalMinutes = Math.Max(1, intervalMinutes);
            if (!enabled)
            {
                NextScheduledAtUtc = null;
            }
        }
    }

    public void Start(string trigger)
    {
        lock (_sync)
        {
            IsRunning = true;
            CurrentTrigger = trigger;
            LastStartedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void SetNextScheduled(DateTimeOffset? nextScheduledAtUtc)
    {
        lock (_sync)
        {
            NextScheduledAtUtc = nextScheduledAtUtc;
        }
    }

    public void Complete(bool succeeded, string message)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastSucceeded = succeeded;
            LastCompletedAtUtc = DateTimeOffset.UtcNow;
            LastMessage = string.IsNullOrWhiteSpace(message) ? "Completed." : message;
            CurrentTrigger = "idle";
        }
    }
}
