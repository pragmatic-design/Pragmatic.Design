namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a service contract the host registers at runtime — through a <c>Use*</c> extension or a
///     host-level registration — rather than one a module declares with <c>[Service]</c>.
/// </summary>
/// <remarks>
///     <para>
///         A module's generator sees the <c>[Service]</c> classes of its own compilation and nothing the
///         host adds when it starts, so a dependency on a contract like this looks unregistered to it
///         and would be reported as <c>PRAG1641</c>, although the running application resolves it. The
///         attribute says it on the contract, where the one who registers it can say it, instead of in a
///         list inside the generator that nobody updates when a new one arrives.
///     </para>
///     <para>
///         <b>Say the lifetime it is registered with.</b> A singleton that takes a per-request contract
///         holds the first request's instance for every request after it, and that check needs the
///         lifetime to run (<c>PRAG1642</c>). The lifetime lives on the contract rather than in a
///         hard-coded list in the generator: a contract missing from such a list would be exempt from
///         the check as well as from <c>PRAG1641</c> — a missing name would buy silence twice.
///     </para>
///     <para>
///         ⚠️ It is a statement about registration, not a check: if the host does not call the extension
///         that registers the contract, the dependency fails when it is first resolved.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [ProvidedByHost(Lifetime.Singleton)]   // UseStorage() registers it
///     public interface IFileStorage;
///
///     [ProvidedByHost(Lifetime.Scoped)]      // one per request, and a singleton must not hold it
///     public interface ITenantContext;
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, Inherited = false)]
public sealed class ProvidedByHostAttribute : Attribute
{
    /// <summary>The contract is registered by the host, with a lifetime it does not state.</summary>
    /// <remarks>
    ///     Leaves <c>PRAG1642</c> unable to speak about it. Prefer the overload that names the lifetime:
    ///     the reason this one exists is that a contract whose lifetime depends on how the host registers
    ///     it — a decorator, a keyed variant — would otherwise have to declare something untrue.
    /// </remarks>
    public ProvidedByHostAttribute()
    {
    }

    /// <summary>The contract is registered by the host, with <paramref name="lifetime" />.</summary>
    public ProvidedByHostAttribute(Lifetime lifetime) => Lifetime = lifetime;

    /// <summary>The lifetime the host registers it with, or <c>null</c> when the contract does not say.</summary>
    public Lifetime? Lifetime { get; }
}
