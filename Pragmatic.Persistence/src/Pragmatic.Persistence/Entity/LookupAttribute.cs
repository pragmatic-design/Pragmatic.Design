namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as a lookup table — small, cacheable, rarely changes.
///     The SG generates <c>ILookupCache&lt;T&gt;</c> registration and navigation
///     properties on consumer entities that have a <c>{TypeName}Id</c> FK.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LookupAttribute : Attribute;
