using System.Collections.Generic;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Renders a trait read DTO from <see cref="TraitDtoShape"/>.
///     <para>
///     The properties and the projection are rendered from the same <see cref="TraitDtoShape"/> that
///     describes them for the manifest. Written out by hand in each template, they would be two lists
///     meant to say the same thing, maintained apart: adding a property to a template would leave the
///     manifest describing the old shape, and a generated client would simply not know the field
///     existed. Rendering from the description leaves one place to change.
///     </para>
/// </summary>
internal abstract class TraitDtoTemplateBase : CSharpTemplate
{
    /// <summary>
    ///     Emits the DTO's properties.
    /// </summary>
    /// <param name="properties">The shape, which is also what the manifest describes.</param>
    /// <param name="useRequired">
    ///     Whether required properties carry the <c>required</c> modifier. It is per-template because
    ///     the four DTOs differ in kind — a record with required members versus a class with defaults —
    ///     and changing that is a source-breaking change for anyone constructing one.
    /// </param>
    protected void RenderDtoProperties(IReadOnlyList<TraitDtoProperty> properties, bool useRequired)
    {
        foreach (var property in properties)
        {
            var modifier = useRequired && property.IsRequired ? "required " : "";
            var initializer = !useRequired && NeedsEmptyStringInitializer(property) ? " = \"\";" : "";
            AppendLine($"public {modifier}{property.Type} {property.Name} {{ get; init; }}{initializer}");
        }
    }

    /// <summary>
    ///     Emits the EF Core projection, reading each property from the same description that declared it.
    /// </summary>
    /// <param name="properties">The shape.</param>
    /// <param name="sourceTypeName">The entity the projection reads from.</param>
    /// <param name="dtoTypeName">The DTO being produced.</param>
    /// <param name="expressionBodied">
    ///     True for <c>Projection =&gt; …</c>, false for <c>Projection { get; } = …</c>. Kept as it was
    ///     per template so this refactor cannot change what consumers already compile against.
    /// </param>
    protected void RenderProjection(
        IReadOnlyList<TraitDtoProperty> properties,
        string sourceTypeName,
        string dtoTypeName,
        bool expressionBodied)
    {
        XmlSummary("EF Core projection expression.");

        var declaration = expressionBodied
            ? $"public static Expression<Func<{sourceTypeName}, {dtoTypeName}>> Projection => e => new {dtoTypeName}"
            : $"public static Expression<Func<{sourceTypeName}, {dtoTypeName}>> Projection {{ get; }} = e => new {dtoTypeName}";

        AppendLine(declaration);
        AppendLine("{");
        IncreaseIndent();
        foreach (var property in properties)
            AppendLine($"{property.Name} = e.{property.Source},");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>
    ///     A non-nullable string with no <c>required</c> modifier needs an initializer, or the consumer
    ///     compiles the generated DTO with a nullability warning it cannot fix.
    /// </summary>
    private static bool NeedsEmptyStringInitializer(TraitDtoProperty property)
        => property.Type is "string";
}
