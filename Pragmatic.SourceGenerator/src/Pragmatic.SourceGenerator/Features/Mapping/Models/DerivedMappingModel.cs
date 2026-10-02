namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     One <c>[MapDerived&lt;TDerivedSource, TDerivedDto&gt;]</c> pair: ready-to-emit fully-qualified
///     type expressions for the runtime dispatch in <c>FromEntity</c>.
/// </summary>
internal sealed record DerivedMappingModel(
    string SourceTypeExpr,
    string DtoTypeExpr);
