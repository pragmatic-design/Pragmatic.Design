using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.DataOwnership;

/// <summary>
///     L2 — mirrors what <c>[HasAccessScopes]</c> gets you in host mode.
///
///     In the host pipeline the SG adds an <c>AccessScopes</c> list and wires
///     a ScopedDataFilter that intersects those tokens with the caller's
///     scopes resolved by <c>IUserScopeResolver</c> (<c>user:…</c>,
///     <c>role:…</c>, <c>scope:…</c>). The sample implements the interface
///     by hand for the reasons spelled out on <see cref="OwnedNote"/>.
/// </summary>
public sealed class ScopedProject : IScopedEntity
{
    public Guid Id { get; set; }
    public List<string> AccessScopes { get; } = [];
    public required string Name { get; set; }
}
