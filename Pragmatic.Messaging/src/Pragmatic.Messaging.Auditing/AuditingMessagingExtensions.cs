using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Messaging.Configuration;

namespace Pragmatic.Messaging.Auditing;

/// <summary>
///     Turns on message auditing.
/// </summary>
public static class AuditingMessagingExtensions
{
    /// <summary>
    ///     Records an audit entry for every handled message, on the framework's audit trail.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The trail itself is not registered here: call <c>AddAuditTrail()</c> from
    ///         <c>Pragmatic.Audit.EFCore</c> (or register your own <c>IAuditTrail</c>). One trail is
    ///         configured once, for the whole application, and messaging is a producer on it.
    ///     </para>
    ///     <para>
    ///         <b>Retention and payload options are gone, deliberately.</b> Retention now belongs to the
    ///         trail (<c>AuditRetentionService</c>), which discards whole sealed segments — deleting
    ///         individual entries would change a sealed segment's hash and make retention
    ///         indistinguishable from tampering. And there is no payload option because there is no
    ///         payload field: storing the serialized message is what put personal data in the old trail.
    ///     </para>
    /// </remarks>
    public static MessagingBuilder EnableAuditing(this MessagingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IMessageMiddleware, AuditMiddleware>();
        return builder;
    }
}
