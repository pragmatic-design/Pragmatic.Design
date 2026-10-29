namespace Pragmatic.Events.Outbox;

/// <summary>
///     Resolves a stored outbox event type string to a concrete <see cref="Type"/> through a
///     <b>closed allowlist</b> built at startup from the registered domain-event handler types.
/// </summary>
/// <remarks>
///     Deserializing into a type obtained from <c>Type.GetType(dbString)</c> is a
///     polymorphic-deserialization gadget surface: a tampered DB row could name an arbitrary
///     type. This resolver is fail-closed — only events that the application actually handles
///     are resolvable; any unknown type name returns <see langword="null"/> and is rejected.
/// </remarks>
public interface IEventOutboxTypeResolver
{
    /// <summary>
    ///     Returns the allowlisted event <see cref="Type"/> for a stored type name, or
    ///     <see langword="null"/> if the name is not in the allowlist (rejected, fail-closed).
    /// </summary>
    /// <param name="storedTypeName">
    ///     The event type an outbox row holds (assembly-qualified, full, or simple name, as
    ///     <see cref="EventOutboxDelivery.Capture"/> writes it).
    /// </param>
    Type? Resolve(string storedTypeName);
}
