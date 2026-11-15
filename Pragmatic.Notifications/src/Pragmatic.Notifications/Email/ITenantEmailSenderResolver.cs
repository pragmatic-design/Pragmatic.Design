namespace Pragmatic.Notifications.Email;

/// <summary>
///     Chooses the address a tenant's notification e-mails leave from. Implement it and register it beside
///     <c>AddSmtp</c> when each tenant sends as itself; without one, every e-mail leaves from
///     <see cref="SmtpOptions.SenderAddress" />.
/// </summary>
/// <remarks>
///     <para>
///         Asked once per e-mail, with the tenant of the recipient the e-mail is for — the tenant the
///         notification was sent under. A recipient with no tenant is not asked about: it gets the options'
///         address.
///     </para>
///     <para>
///         <see langword="null" /> keeps the options' address for that tenant, so a resolver that knows
///         only some tenants answers for those and leaves the rest as they were.
///     </para>
///     <para>
///         ⚠️ The e-mail channel is a singleton, so the resolver is too: one that reads the tenant's row
///         from a database opens its own scope.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     services.AddNotifications(n => n.AddSmtp(sender => sender.SenderAddress = "noreply@example.com", smtp => …));
///     services.AddSingleton&lt;ITenantEmailSenderResolver, CompanySenders&gt;();
///     </code>
/// </example>
public interface ITenantEmailSenderResolver
{
    /// <summary>The sender for <paramref name="tenantId" />, or <see langword="null" /> for the options' one.</summary>
    ValueTask<EmailSenderIdentity?> ResolveAsync(string tenantId, CancellationToken ct = default);
}
