namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One parameter of a promoted specification, as it appears on the derived query.
/// </summary>
/// <param name="TypeName">The parameter's type, fully qualified.</param>
/// <param name="ParameterName">The name the specification gave it — the call site keeps it.</param>
/// <param name="PropertyName">The same name in Pascal case, which is what the wire and the route see.</param>
/// <param name="IsNullable">Whether the type admits null, which decides the property's initialiser.</param>
internal sealed record DerivedQueryInput(
    string TypeName,
    string ParameterName,
    string PropertyName,
    bool IsNullable);
