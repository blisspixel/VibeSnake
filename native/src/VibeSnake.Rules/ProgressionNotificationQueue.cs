namespace VibeSnake.Rules;

public sealed record ProgressionNotification(
    string Id,
    string Caption,
    int MinimumVisibleMilliseconds,
    bool MotionEnabled);

/// <summary>
/// Bounded FIFO for progression celebrations. It deduplicates pending IDs and
/// preserves readable time while reduced motion disables transition movement.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The established public type name is retained for API compatibility.")]
public sealed class ProgressionNotificationQueue
{
    public const int MaximumPending = 16;
    public const int MinimumReadableMilliseconds = 3_000;

    private readonly Queue<ProgressionNotification> _pending = new();
    private readonly HashSet<string> _pendingIds = new(StringComparer.Ordinal);

    public int Count => _pending.Count;

    public ProgressionNotification? Current =>
        _pending.TryPeek(out var notification) ? notification : null;

    public bool Enqueue(string id, string caption, bool reducedMotion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        if (_pendingIds.Contains(id) || _pending.Count >= MaximumPending)
        {
            return false;
        }

        _pending.Enqueue(new ProgressionNotification(
            id,
            caption,
            MinimumReadableMilliseconds,
            MotionEnabled: !reducedMotion));
        _pendingIds.Add(id);
        return true;
    }

    public bool TryDequeue(out ProgressionNotification? notification)
    {
        if (!_pending.TryDequeue(out notification))
        {
            return false;
        }

        _pendingIds.Remove(notification.Id);
        return true;
    }

    public void Clear()
    {
        _pending.Clear();
        _pendingIds.Clear();
    }
}
