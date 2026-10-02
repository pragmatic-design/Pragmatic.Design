using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads an <c>[GeneratedValue(format)]</c> on a property that opts into auto-generation and produces an
///     <see cref="GeneratedValueModel" /> for the generated default-value generator.
/// </summary>
/// <remarks>
///     Formats containing a <c>{SEQ:N}</c> database-sequence token are skipped here: sequence-backed
///     generation needs a database round-trip and is handled separately, so this transform only covers
///     the self-contained app-side formats (date / <c>{RANDOM:N}</c> / <c>{GUID:N}</c>).
/// </remarks>
internal static class GeneratedValueTransform
{
    public const string GeneratedValueAttributeName = "Pragmatic.Persistence.Entity.GeneratedValueAttribute";

    public static GeneratedValueModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not IPropertySymbol property)
            return null;

        if (property.ContainingType is not { } entity)
            return null;

        // Auto-generation formats to a string; a property of any other type is left untouched.
        if (property.Type.SpecialType != SpecialType.System_String)
            return null;

        var attribute = context.Attributes[0];

        // AutoGenerate defaults to true; skip when explicitly disabled.
        var autoGenerate = true;
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == "AutoGenerate" && named.Value.Value is bool b)
                autoGenerate = b;
        }

        if (!autoGenerate)
            return null;

        if (attribute.ConstructorArguments.Length == 0 ||
            attribute.ConstructorArguments[0].Value is not string format ||
            string.IsNullOrWhiteSpace(format))
            return null;

        var segments = GeneratedValueFormat.Parse(format);
        if (segments.Length == 0)
            return null;

        var entityFullName = entity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        var ns = entity.ContainingNamespace is { IsGlobalNamespace: false } n
            ? n.ToDisplayString()
            : string.Empty;

        // {SEQ:N} needs a DB round-trip: resolve the sequence name (explicit or default) and the
        // entity's boundary so the generated generator can inject the boundary-keyed DbContext.
        var hasSequence = GeneratedValueFormat.HasSequence(segments);
        string? sequenceName = null;
        string? boundary = null;
        if (hasSequence)
        {
            sequenceName = attribute.NamedArguments
                .FirstOrDefault(na => na.Key == "SequenceName").Value.Value as string;
            if (string.IsNullOrWhiteSpace(sequenceName))
                sequenceName = $"{entity.Name}_{property.Name}_seq";
            boundary = EntityTransform.GetBoundaryInfo(entity).FullTypeName;
        }

        return new GeneratedValueModel
        {
            EntityFullName = entityFullName,
            EntityShortName = entity.Name,
            Namespace = ns,
            PropertyName = property.Name,
            GeneratorClassName = GeneratedValueNaming.GeneratorClassName(entity.Name, property.Name),
            SetterName = property.SetMethod?.DeclaredAccessibility == Accessibility.Public
                ? property.Name
                : $"Set{property.Name}",
            Segments = segments,
            HasSequence = hasSequence,
            SequenceName = sequenceName,
            TargetBoundaryFullName = boundary
        };
    }
}
