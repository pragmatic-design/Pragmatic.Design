namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a class as a domain entity.
/// </summary>
/// <remarks>
///     <para>
///         The generator writes the identity onto the type: a <c>PersistenceId</c> of type
///         <see cref="System.Guid" />, initialised to a version 7 value, with <c>Id</c> as its alias.
///         Equality is not generated: an entity is a class and compares by reference.
///     </para>
///     <para>
///         ⚠️ <b>The identifier type is not a choice.</b> This attribute takes no type parameter,
///         because every non-Guid answer is worse than the default in the same three ways: a natural
///         key propagates into every foreign key, so the day it changes (and natural keys change) the
///         cost is the whole schema; a string key scatters the primary index where a version 7 Guid
///         keeps it time-ordered and compact; and on an <c>ITenantEntity</c> a domain-assigned
///         identifier <em>collides between tenants</em>, which only a composite primary key could fix
///         — meaning two-column foreign keys everywhere.
///     </para>
///     <para>
///         What a type parameter would be reached for — carrying an identifier from a system being
///         replaced — belongs to the domain, not to identity: keep it as an ordinary property with its
///         own uniqueness (<c>[Unique(nameof(LegacyCode))]</c>), where it can be queried, corrected
///         and eventually dropped without touching a single foreign key.
///     </para>
///     <example>
///         <code>
/// [Entity]
/// public partial class Order
/// {
///     [LogicKey]
///     public string OrderNumber { get; set; }
///     public decimal Total { get; set; }
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityAttribute : Attribute;
