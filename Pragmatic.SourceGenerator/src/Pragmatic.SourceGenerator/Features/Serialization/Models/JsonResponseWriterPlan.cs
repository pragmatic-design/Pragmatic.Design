using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>The generated writer of one response type: the method that writes it, and every method it needs.</summary>
/// <param name="EntryMethod">The method that writes the response type.</param>
/// <param name="TypeExpr">The response type, fully qualified.</param>
/// <param name="Methods">Every method the plan needs, the entry one included.</param>
/// <param name="NeedsInfrastructureExclusion">
///     Whether the plan leaves out members the host strips only when it has persistence
///     (<c>EntityJsonModifier.ExcludeInfrastructureProperties</c>). A writer that does is used only by a host
///     that strips them; one that does not writes the same thing either way.
/// </param>
internal sealed record JsonResponseWriterPlan(
    string EntryMethod,
    string TypeExpr,
    EquatableArray<JsonWriterMethodModel> Methods,
    bool NeedsInfrastructureExclusion);
