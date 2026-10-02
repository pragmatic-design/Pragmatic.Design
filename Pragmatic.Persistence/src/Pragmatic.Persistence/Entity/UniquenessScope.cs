namespace Pragmatic.Persistence.Entity;

/// <summary>
///     How far a domain key has to be unique.
/// </summary>
/// <remarks>
///     <para>
///         Only meaningful on an entity that implements <c>ITenantEntity</c>. Everywhere else there is
///         one scope and this changes nothing.
///     </para>
/// </remarks>
public enum UniquenessScope
{
    /// <summary>
    ///     Unique within the tenant. The generated unique index leads with <c>TenantId</c>.
    /// </summary>
    /// <remarks>
    ///     The default, and what a domain key almost always means: two workspaces both have a term
    ///     called "kanban", a role called "reviewer", a member with a given email. Uniqueness that
    ///     reached across tenants would let one of them take a value away from the other — and say so,
    ///     since the conflict tells the second tenant that somebody already holds it.
    /// </remarks>
    PerTenant = 0,

    /// <summary>
    ///     Unique across every tenant.
    /// </summary>
    /// <remarks>
    ///     For the values that identify the same thing to everyone — a national tax number, an
    ///     externally issued identifier — where a duplicate in a second tenant would be the same
    ///     mistake as a duplicate in the first. ⚠️ It also means one tenant can refuse a value to
    ///     another, and learn from the refusal that it exists. Say it deliberately.
    /// </remarks>
    Global = 1
}
