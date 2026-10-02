namespace Pragmatic.SourceGenerator.Features.Resource.Models;

/// <summary>
///     The type an operation answers with, once the developer's <c>[ReturnsDto&lt;T&gt;]</c> and the
///     scaffolded default have been resolved against each other.
/// </summary>
/// <remarks>
///     It exists so that resolution happens once, in <see cref="ResourceCrudModel.DtoFor"/>, and not
///     again in each of the five places that name a DTO — the query attribute, the query model, the
///     endpoint, the mapping and the manifest. Five copies of one rule is how the shape a query
///     declares and the shape it projects end up disagreeing, and that disagreement only shows at
///     runtime, as an empty result.
/// </remarks>
internal readonly record struct ResourceDtoRef
{
    /// <summary>Namespace-qualified, without <c>global::</c>.</summary>
    public required string FullTypeName { get; init; }

    public required string TypeName { get; init; }

    /// <summary>Whether this generator is the one writing it.</summary>
    public required bool IsScaffolded { get; init; }

    /// <summary>The form that binds from any namespace.</summary>
    public string Qualified => $"global::{FullTypeName}";
}
