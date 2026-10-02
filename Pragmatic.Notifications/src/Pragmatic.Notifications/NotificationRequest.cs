namespace Pragmatic.Notifications;

/// <summary>
///     A request to send a notification. V1 uses Content directly; V2 will add TemplateId + Data.
/// </summary>
public sealed record NotificationRequest
{
    /// <summary>Target audience — determines recipient resolution strategy.</summary>
    public required NotificationAudience Audience { get; init; }

    /// <summary>Recipient specification (user, role, tenant, direct address).</summary>
    public required NotificationRecipient Recipient { get; init; }

    /// <summary>Notification content (V1: strings provided by developer).</summary>
    /// <remarks>
    ///     Still required, and still what every recipient receives unless <see cref="ContentFor" />
    ///     says otherwise. It is also what <see cref="ContentFor" /> falls back to, so a request that
    ///     supplies both always has something to send.
    /// </remarks>
    public required NotificationContent Content { get; init; }

    /// <summary>
    ///     Produces the content for one resolved recipient — the way a notification gets written in
    ///     the language of whoever receives it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ResolvedRecipient.Locale" /> is the argument that matters, and it is the
    ///         reason this exists: the resolver fills it from the recipient's preferences, and until
    ///         now nothing could use it. The pipeline resolves recipients and then delivers, so a
    ///         <see cref="Content" /> built by the caller was necessarily rendered before any
    ///         recipient existed — one message for a role, a tenant or a list of users, whatever
    ///         their preferences said.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Optional, and the default is unchanged behaviour.</b> Left null, every recipient
    ///         is delivered <see cref="Content" /> exactly as before. No existing caller has to move.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Do not use this to loop in the caller instead.</b> A caller that renders per
    ///         recipient has to resolve recipients first, which is the responsibility this pipeline
    ///         exists to hold — it would have to re-implement preferences, opt-outs and channel
    ///         routing to know who it is rendering for. This runs where those answers already are.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>With <c>EnqueueAsync</c> it runs later, on the delivery worker.</b> The request
    ///         is queued in memory and delivered outside the scope that created it, so a delegate
    ///         closing over a scoped service — a <c>DbContext</c>, a per-request localizer — captures
    ///         something that will have been disposed. Close over data, not over services.
    ///     </para>
    /// </remarks>
    public Func<ResolvedRecipient, CancellationToken, ValueTask<NotificationContent>>? ContentFor { get; init; }

    /// <summary>Priority — affects channel routing (Critical → all channels).</summary>
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;

    /// <summary>Override automatic channel routing.</summary>
    public NotificationChannel? ChannelOverride { get; init; }

    /// <summary>Category for preference-based routing (e.g., "marketing", "transactional").</summary>
    public string? Category { get; init; }

    /// <summary>Arbitrary metadata attached to the notification.</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
