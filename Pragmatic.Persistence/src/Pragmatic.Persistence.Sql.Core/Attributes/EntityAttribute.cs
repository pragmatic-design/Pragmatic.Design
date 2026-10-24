namespace Pragmatic.Persistence.Sql.Attributes;

// ASSUMPTION (2026-10-18): the spike declares its own [Entity] and [Table] so it builds without the
// repository's projects. The generator matches the attributes by simple name ("EntityAttribute",
// "TableAttribute"), so the repository's Pragmatic.Persistence attributes are a drop-in replacement.

/// <summary>Marks a class whose LINQ call sites the generator translates to SQL.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityAttribute : Attribute;
