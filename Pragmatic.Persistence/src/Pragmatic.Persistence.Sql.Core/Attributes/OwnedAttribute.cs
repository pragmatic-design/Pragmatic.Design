namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A type with no table of its own, flattened into its owner's: each mapped property becomes a column named
///     <c>{Property}_{Member}</c> (the repository's <c>OwnsOne</c> convention). An owner property typed <c>T?</c> is optional: its
///     columns are null together, and the object is null when they are. Members are read in predicates and projections as
///     <c>o.Address.City</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OwnedAttribute : Attribute;
