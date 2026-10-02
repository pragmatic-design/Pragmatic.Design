using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Pragmatic.Notifications.Diagnostics;
using Pragmatic.Notifications.Channels;
using Pragmatic.Notifications.Preferences;
using Pragmatic.Notifications.Tracking;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Notifications.Pipeline;

/// <summary>
///     Orchestrates the notification delivery pipeline: validate → resolve → route → deliver → track.
/// </summary>
internal sealed partial class NotificationPipeline(
    IRecipientResolver resolver,
    INotificationRouter router,
    INotificationChannelFactory channelFactory,
    INotificationStore store,
    ILogger<NotificationPipeline> logger)
{
    public Task<NotificationResult> ExecuteAsync(NotificationRequest request, CancellationToken ct = default)
        => ExecuteAsync(request, trackingId: null, ct);

    /// <param name="request">The notification to deliver.</param>
    /// <param name="trackingId">
    ///     Id of the Pending tracking record created by <c>EnqueueAsync</c>, if any. The pipeline
    ///     settles its final status so the caller-visible record never stays Pending forever.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<NotificationResult> ExecuteAsync(NotificationRequest request, Guid? trackingId, CancellationToken ct = default)
    {
        using var activity = NotificationsDiagnostics.ActivitySource.StartActivity(
            "Notifications.Deliver", ActivityKind.Producer);
        activity?.SetTag(NotificationTags.Audience, request.Audience.ToString());
        activity?.SetTag(NotificationTags.Priority, request.Priority.ToString());

        // 1. Validate
        var validationError = Validate(request);
        if (validationError is not null)
            return await FailAsync(trackingId, validationError, ct).ConfigureAwait(false);

        // 2. Resolve recipients
        var resolved = await resolver.ResolveAsync(request.Recipient, request.Audience, ct).ConfigureAwait(false);
        if (resolved.Count == 0)
        {
            LogNoRecipients(request.Audience);
            return await FailAsync(
                trackingId,
                "No recipients resolved for the given audience and recipient. The built-in resolver only "
                + "handles direct addresses (EmailAddress, PhoneNumber, WebhookUrl); resolving UserId, "
                + "UserIds, RoleName or TenantId to delivery addresses requires a custom IRecipientResolver.",
                ct).ConfigureAwait(false);
        }

        var notificationId = trackingId ?? Guid.CreateVersion7();
        var deliveryIds = new List<Guid>();
        var errors = new List<string>();
        var suppressedByPreferences = false;

        // 3-4. Route + Deliver per recipient
        foreach (var recipient in resolved)
        {
            // Preferences are enforced here rather than only inside the router, because
            // ChannelOverride skips routing altogether: an opt-out, a muted category or
            // do-not-disturb must hold regardless of how the channel was chosen.
            if (NotificationPreferenceRules.IsSuppressed(recipient.Preferences, request.Priority, request.Category))
            {
                suppressedByPreferences = true;
                LogSuppressedByPreferences(recipient.Address);
                continue;
            }

            var channels = request.ChannelOverride ?? router.Route(recipient, request.Priority, request.Category);
            if (channels == NotificationChannel.None)
            {
                suppressedByPreferences = true;
                continue;
            }

            // The message for THIS recipient. ResolvedRecipient.Locale has been carried since the
            // resolver was written and read by nothing: the content arrived already rendered, before
            // any recipient existed, so a notification addressed to a role or a tenant was one set of
            // sentences for everyone whatever their preferences said.
            //
            // ⚠️ Once per recipient and not once per channel. A person who receives the same
            // notification by email and by SMS reads one message in two shapes, not two messages; and
            // a factory that hits a database would otherwise be asked the same question twice.
            var content = request.ContentFor is null
                ? request.Content
                : await request.ContentFor(recipient, ct).ConfigureAwait(false);

            // Deliver to each channel in the flags
            foreach (var channelFlag in GetIndividualChannels(channels))
            {
                var channel = channelFactory.GetChannel(channelFlag, recipient.TenantId);
                if (channel is null)
                {
                    LogChannelNotRegistered(channelFlag);
                    continue;
                }

                var record = new NotificationRecord
                {
                    Audience = request.Audience,
                    RecipientAddress = recipient.Address,
                    Channel = channelFlag,
                    // What was sent, not what would have been sent had nobody translated it: one
                    // record already exists per delivery, so the trail can afford to agree with the
                    // mailbox.
                    Subject = content.Subject,
                    Status = DeliveryStatus.Pending,
                    TenantId = recipient.TenantId,
                    Category = request.Category,
                    Metadata = request.Metadata,
                };

                record = await store.CreateAsync(record, ct).ConfigureAwait(false);
                deliveryIds.Add(record.Id);

                var channelTag = new KeyValuePair<string, object?>("channel", channelFlag.ToString());
                var timestamp = Stopwatch.GetTimestamp();

                try
                {
                    var result = await channel.DeliverAsync(recipient, content, ct).ConfigureAwait(false);

                    NotificationsDiagnostics.DeliveryDuration.Record(
                        Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, channelTag);

                    if (result.Success)
                    {
                        await store.UpdateStatusAsync(record.Id, DeliveryStatus.Sent, null, ct).ConfigureAwait(false);
                        NotificationsDiagnostics.ChannelDeliveries.Add(1, channelTag);
                        LogDeliverySuccess(channelFlag, recipient.Address);
                    }
                    else
                    {
                        await store.UpdateStatusAsync(record.Id, DeliveryStatus.Failed, result.ErrorMessage, ct).ConfigureAwait(false);
                        NotificationsDiagnostics.ChannelFailures.Add(1, channelTag);
                        errors.Add($"{channelFlag}: {result.ErrorMessage}");
                        LogDeliveryFailed(channelFlag, recipient.Address, result.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    await store.UpdateStatusAsync(record.Id, DeliveryStatus.Failed, ex.Message, ct).ConfigureAwait(false);
                    NotificationsDiagnostics.ChannelFailures.Add(1, channelTag);
                    errors.Add($"{channelFlag}: {ex.Message}");
                    LogDeliveryException(channelFlag, recipient.Address, ex);
                }
            }
        }

        // 5. Result + settle the EnqueueAsync tracking record (if any).
        // deliveryIds counts every attempt, errors the failed ones — partial success is
        // surfaced as Success=true WITH Errors populated, never masked as a clean success.
        var succeededCount = deliveryIds.Count - errors.Count;

        NotificationResult outcome;
        if (deliveryIds.Count == 0)
        {
            // Nothing was even attempted. Reporting success here would tell the caller the
            // notification went out while it silently evaporated — the typical symptom of an app
            // that forgot to register a channel. Fail, and say which of the two causes it was.
            var reason = suppressedByPreferences
                ? "No notification was sent: every channel was suppressed by the recipient's preferences."
                : "No notification was sent: no delivery channel is registered for this application. "
                  + "Register one (for example AddSmtp or AddWebhook) when configuring notifications.";

            LogNothingDelivered(reason);
            NotificationsDiagnostics.NotificationsFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, reason);
            return await FailAsync(trackingId, reason, ct).ConfigureAwait(false);
        }

        if (errors.Count == 0)
        {
            NotificationsDiagnostics.NotificationsSent.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok);
            outcome = NotificationResult.Succeeded(notificationId, deliveryIds);
        }
        else if (succeededCount > 0)
        {
            NotificationsDiagnostics.NotificationsSent.Add(1);
            activity?.SetStatus(ActivityStatusCode.Ok, "partial delivery");
            outcome = NotificationResult.Partial(notificationId, deliveryIds, [.. errors]);
        }
        else
        {
            NotificationsDiagnostics.NotificationsFailed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error, string.Join("; ", errors));
            outcome = NotificationResult.Failed([.. errors]);
        }

        if (trackingId is { } rootId)
        {
            // deliveryIds is non-empty here: the "nothing attempted" case returned earlier.
            var (rootStatus, rootError) = (succeededCount, errors.Count) switch
            {
                (> 0, 0) => (DeliveryStatus.Sent, (string?)null),
                (> 0, > 0) => (DeliveryStatus.Sent, $"Partial: {errors.Count}/{deliveryIds.Count} deliveries failed — {string.Join("; ", errors)}"),
                _ => (DeliveryStatus.Failed, string.Join("; ", errors)),
            };

            await store.UpdateStatusAsync(rootId, rootStatus, rootError, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>Settles the tracking record (when present) and returns a failed result.</summary>
    private async Task<NotificationResult> FailAsync(Guid? trackingId, string error, CancellationToken ct)
    {
        if (trackingId is { } rootId)
            await store.UpdateStatusAsync(rootId, DeliveryStatus.Failed, error, ct).ConfigureAwait(false);

        return NotificationResult.Failed(error);
    }

    private static string? Validate(NotificationRequest request)
    {
        if (request.Content.Subject.Length == 0)
            return "Notification subject is required.";

        if (request.Content.Body.Length == 0)
            return "Notification body is required.";

        return null;
    }

    private static readonly NotificationChannel[] AllChannels =
        Enum.GetValues<NotificationChannel>().Where(c => c != NotificationChannel.None).ToArray();

    private static IEnumerable<NotificationChannel> GetIndividualChannels(NotificationChannel flags)
    {
        foreach (var value in AllChannels)
        {
            if (flags.HasFlag(value))
                yield return value;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No recipients resolved for audience {Audience}")]
    private partial void LogNoRecipients(NotificationAudience audience);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification not delivered: {Reason}")]
    private partial void LogNothingDelivered(string reason);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification suppressed by recipient preferences for {Address}")]
    private partial void LogSuppressedByPreferences(string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel {Channel} is not registered — skipping")]
    private partial void LogChannelNotRegistered(NotificationChannel channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification delivered via {Channel} to {Address}")]
    private partial void LogDeliverySuccess(NotificationChannel channel, string address);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification delivery failed via {Channel} to {Address}: {Error}")]
    private partial void LogDeliveryFailed(NotificationChannel channel, string address, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification delivery exception via {Channel} to {Address}")]
    private partial void LogDeliveryException(NotificationChannel channel, string address, Exception ex);
}
