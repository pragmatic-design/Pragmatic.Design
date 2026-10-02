using Pragmatic.Composition;

namespace Pragmatic.Messaging;

/// <summary>
///     Package definition for messaging infrastructure entities.
///     Import via <c>[UsePackage&lt;MessagingPackage&gt;]</c> on your module to activate
///     outbox, dead letter, and audit entities in your DbContext.
/// </summary>
/// <remarks>
///     Without UsePackage, messaging works fully in-memory.
///     With UsePackage, the SG generates EntityConfig, Repository, and endpoints.
/// </remarks>
public sealed class MessagingPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Pragmatic.Messaging";

    /// <inheritdoc />
    public static string? RoutePrefix => "admin/messaging";

    /// <inheritdoc />
    public static string? Description => "Messaging infrastructure: outbox, dead letter, audit trail";
}
