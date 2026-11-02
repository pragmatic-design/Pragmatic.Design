namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A property with a private setter the rows are built through: the helper's name, the declaring type, the property's type and name, and
///     whether the declaring type is sealed (no derived row to keep the type of: the helper is not generic, a sealed type is no constraint).
/// </summary>
internal sealed record PrivateSetterModel(string Name, string Owner, string Type, string Property, bool OwnerIsSealed);
