using System.Collections.Concurrent;

namespace Pragmatic.Notifications.Testing;

/// <summary>
///     Test harness that records all notification sends for assertions. Replaces INotificationService in tests.
/// </summary>
public sealed class NotificationTestHarness : INotificationService
{
    private readonly ConcurrentBag<SentNotification> _sent = [];

    /// <summary>All notifications sent so far.</summary>
    public IReadOnlyList<SentNotification> Sent => [.. _sent];

    /// <summary>Returns true if any notification was sent to the given audience.</summary>
    public bool HasSentTo(NotificationAudience audience)
        => _sent.Any(n => n.Request.Audience == audience);

    /// <summary>Returns true if any notification was sent with the given subject.</summary>
    public bool HasSentWithSubject(string subject)
        => _sent.Any(n => n.Request.Content.Subject == subject);

    /// <summary>Returns true if any notification matches the predicate.</summary>
    public bool HasSent(Func<NotificationRequest, bool> predicate)
        => _sent.Any(n => predicate(n.Request));

    /// <summary>Returns all notifications matching the predicate.</summary>
    public IReadOnlyList<SentNotification> SentWhere(Func<NotificationRequest, bool> predicate)
        => _sent.Where(n => predicate(n.Request)).ToList();

    /// <summary>Clears all recorded notifications.</summary>
    public void Reset() => _sent.Clear();

    public Task<NotificationResult> SendAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        _sent.Add(new SentNotification(request, id, DateTimeOffset.UtcNow, Synchronous: true));
        return Task.FromResult(NotificationResult.Succeeded(id));
    }

    public Task<NotificationResult> EnqueueAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        _sent.Add(new SentNotification(request, id, DateTimeOffset.UtcNow, Synchronous: false));
        return Task.FromResult(NotificationResult.Succeeded(id));
    }
}

/// <summary>
///     Recorded notification for test assertions.
/// </summary>
public sealed record SentNotification(
    NotificationRequest Request,
    Guid NotificationId,
    DateTimeOffset SentAt,
    bool Synchronous);
