namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A property with a private setter the rows are built through: the helper's name, the declaring type, the property's type and name.</summary>
internal sealed record PrivateSetterModel(string Name, string Owner, string Type, string Property);
