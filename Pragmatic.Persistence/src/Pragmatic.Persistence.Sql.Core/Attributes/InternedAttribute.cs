namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a string property whose values repeat across rows (a status code, a country, a name a page of rows shares): the reader
///     hands out one instance per distinct value from a bounded pool, so the rows retain one string per value instead of one per
///     row (the provider still materializes each value as it reads it: what is saved is what the rows keep, not the allocation).
///     Projections that read the column (<c>o.Customer!.Name</c>) and the runtime path go through the same pool.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class InternedAttribute : Attribute;
