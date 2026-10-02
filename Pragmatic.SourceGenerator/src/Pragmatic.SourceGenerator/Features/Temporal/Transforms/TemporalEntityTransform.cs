using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Temporal.Models;

namespace Pragmatic.SourceGenerator.Features.Temporal.Transforms;

/// <summary>
///     Finds the properties whose type is one of the eight temporal types.
/// </summary>
/// <remarks>
///     The same set <c>TemporalPropertyConfigurator.IsTemporalType</c> tests at run time, tested here
///     instead. Kept in sync by name: a ninth temporal type added there and not here would simply not
///     be registered, and its column would go missing rather than be mistyped.
/// </remarks>
internal static class TemporalEntityTransform
{
    private static readonly HashSet<string> TemporalTypes = new(StringComparer.Ordinal)
    {
        "Pragmatic.Temporal.Types.LocalDate",
        "Pragmatic.Temporal.Types.LocalTime",
        "Pragmatic.Temporal.Types.LocalDateTime",
        "Pragmatic.Temporal.Types.ZonedDateTime",
        "Pragmatic.Temporal.Types.Duration",
        "Pragmatic.Temporal.Types.Period",
        "Pragmatic.Temporal.Types.DateRange",
        "Pragmatic.Temporal.Types.CronExpression"
    };

    public static TemporalEntityModel? From(GeneratorSyntaxContext context)
    {
        if (context.Node is not TypeDeclarationSyntax declaration)
            return null;

        if (ModelExtensions.GetDeclaredSymbol(context.SemanticModel, declaration) is not INamedTypeSymbol symbol)
            return null;

        // The registration class is a sibling at namespace scope, so it must be able to name the type.
        if (symbol.IsAbstract || !IsNameableFromNamespaceScope(symbol))
            return null;

        var builder = ImmutableArray.CreateBuilder<TemporalPropertyEntryModel>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol { IsStatic: false, IsIndexer: false } property)
                continue;
            if (property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null)
                continue;
            if (!IsTemporal(property.Type))
                continue;

            builder.Add(new TemporalPropertyEntryModel(
                property.Name,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        if (builder.Count == 0)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();

        return new TemporalEntityModel(ns, symbol.Name, UniqueName(symbol),
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), builder.ToImmutable());
    }

    /// <summary>True for a temporal type, or a Nullable of one.</summary>
    private static bool IsTemporal(ITypeSymbol type)
    {
        var unwrapped = type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && type is INamedTypeSymbol { TypeArguments.Length: 1 } nullable
            ? nullable.TypeArguments[0]
            : type;

        // WithNullableAnnotation(None) so `CronExpression?` — a nullable REFERENCE type, where the
        // annotation rides on the symbol rather than wrapping it — still matches.
        return TemporalTypes.Contains(
            unwrapped.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString());
    }

    /// <summary>The name including outer types, so two nested types cannot collide on the hint name.</summary>
    private static string UniqueName(INamedTypeSymbol symbol)
    {
        var names = new List<string> { symbol.Name };
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
            names.Insert(0, outer.Name);

        return string.Join("_", names);
    }

    private static bool IsNameableFromNamespaceScope(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
                return false;
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                return false;
        }

        return true;
    }
}
