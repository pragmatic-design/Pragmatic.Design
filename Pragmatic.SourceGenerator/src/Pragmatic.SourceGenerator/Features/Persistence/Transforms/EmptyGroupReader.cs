using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads a view's <c>[GroupBy(IncludeEmpty = true)]</c> (#766): the key's values, known at compile time, or what makes the view unable to
///     answer an empty group.
/// </summary>
/// <remarks>
///     The values are the enum's members, written into the generated <c>WithEmptyGroups</c>: decided here, where the type is known, and not
///     looked up at run time.
/// </remarks>
internal static class EmptyGroupReader
{
    private const string GroupByAttributeName = "GroupByAttribute";

    /// <summary>The opt-in, the values to fill from, and the refusals, for a view whose keys are <paramref name="keys" />.</summary>
    public static (bool IncludeEmpty, string[] Values, string? KeyType, EmptyGroupRefusalModel[] Refusals) Read(
        INamedTypeSymbol view, IReadOnlyList<GroupByModel> keys, CancellationToken ct)
    {
        var optIn = view.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { Name: GroupByAttributeName, TypeArguments.Length: 1 }
            && a.NamedArguments.Any(n => n is { Key: "IncludeEmpty", Value.Value: true }));
        if (optIn is null)
            return (false, [], null, []);

        var location = LocationInfo.From(optIn.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation());
        var refusals = new List<EmptyGroupRefusalModel>();
        var values = new List<string>();
        string? keyName = null;
        string? keyType = null;

        if (keys.Count != 1)
        {
            refusals.Add(new EmptyGroupRefusalModel
            {
                Kind = EmptyGroupRefusalKind.CompositeKey,
                Detail = string.Join(", ", keys.Select(k => $"'{k.PropertyName}'")),
                Location = location
            });
        }
        else
        {
            var key = keys[0];
            keyName = key.PropertyName;
            var projected = key.IsProjected ? ViewProperty(view, key.PropertyName) : null;

            if (projected?.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            {
                var enumName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                keyType = enumName;
                values.AddRange(enumType.GetMembers().OfType<IFieldSymbol>()
                    .Where(f => f.HasConstantValue)
                    .Select(f => $"{enumName}.{f.Name}"));
            }
            else
            {
                refusals.Add(new EmptyGroupRefusalModel
                {
                    Kind = EmptyGroupRefusalKind.KeyNotAProjectedEnum,
                    Detail = projected is null
                        ? $"'{key.PropertyName}', which no property of the view carries"
                        : $"'{key.PropertyName}' of type '{projected.Type.ToDisplayString()}'",
                    Location = location
                });
            }
        }

        foreach (var property in view.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.Name == keyName)
                continue;

            if (NoValueForAnEmptyGroup(property) is { } declared && !IsNullable(property.Type))
            {
                refusals.Add(new EmptyGroupRefusalModel
                {
                    Kind = EmptyGroupRefusalKind.NoValueNotNullable,
                    Detail = $"'{property.Name}' ([{declared}], {property.Type.ToDisplayString()})",
                    Location = LocationInfo.From(property.Locations.Length > 0 ? property.Locations[0] : null)
                });
            }
        }

        return refusals.Count == 0 ? (true, [.. values], keyType, []) : (true, [], null, [.. refusals]);
    }

    /// <summary>The attribute of a member an empty group has no value for (Min, Max, Avg, a From), or null.</summary>
    private static string? NoValueForAnEmptyGroup(IPropertySymbol property)
        => property.GetAttributes()
            .Select(a => a.AttributeClass?.Name)
            .FirstOrDefault(n => n is "MinAttribute" or "MaxAttribute" or "AvgAttribute" or "FromAttribute") is { } name
            ? name.Substring(0, name.Length - "Attribute".Length)
            : null;

    private static IPropertySymbol? ViewProperty(INamedTypeSymbol view, string name)
        => view.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault(p => p.DeclaredAccessibility == Accessibility.Public);

    private static bool IsNullable(ITypeSymbol type)
        => type.NullableAnnotation == NullableAnnotation.Annotated
           || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
}
