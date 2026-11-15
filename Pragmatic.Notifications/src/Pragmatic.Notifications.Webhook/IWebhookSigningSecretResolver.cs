namespace Pragmatic.Notifications.Webhook;

/// <summary>
///     Chooses the secret a delivery to one destination is signed with, so each receiver verifies with a
///     secret of its own and can rotate it without the others. Without one — or when it answers
///     <see langword="null" /> — the delivery is signed with <see cref="WebhookOptions.SigningSecret" />.
/// </summary>
/// <remarks>
///     <para>
///         Asked once per delivery, before the first attempt: every retry of it is signed with the same
///         secret.
///     </para>
///     <para>
///         ⚠️ The webhook channel is a singleton, so the resolver is too: one that reads a subscription's
///         row opens its own scope.
///     </para>
/// </remarks>
public interface IWebhookSigningSecretResolver
{
    /// <summary>The secret for <paramref name="destination" />, or <see langword="null" /> for the application's.</summary>
    /// <param name="destination">The URL the delivery is posted to.</param>
    /// <param name="tenantId">The tenant the notification was sent under, or <see langword="null" />.</param>
    /// <param name="ct">Cancellation.</param>
    ValueTask<string?> ResolveAsync(Uri destination, string? tenantId, CancellationToken ct = default);
}
