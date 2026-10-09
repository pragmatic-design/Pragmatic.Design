using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Analysis;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Analysis;

/// <summary>
///     The members System.Text.Json's reflection resolver writes for an object type under the host's options, in
///     the order and under the names it writes them.
/// </summary>
/// <remarks>
///     <para>
///         The reflection resolver's rules, not the generated JSON context's: a type no context covers is written
///         by reflection, and that is the output a response writer has to be identical to. Where the two would
///         differ the planner refuses the type rather than pick one (see <see cref="JsonResponseWriterPlanner" />).
///     </para>
///     <para>
///         What it reads: public instance properties with a public getter, the most derived declaration of a name
///         first and then its bases; <c>[JsonIgnore]</c> and its condition; <c>[JsonPropertyName]</c>, otherwise
///         <see cref="CamelCase" />; <c>[JsonPropertyOrder]</c>. What it refuses, because a writer generated from
///         the declaration would not reproduce it: a converter, extension data, number handling, a member included
///         from outside the public surface, two members on one wire name.
///     </para>
/// </remarks>
internal static class JsonResponseMemberReader
{
    private const string JsonNamespace = "System.Text.Json.Serialization";

    /// <summary>One member the resolver writes.</summary>
    public sealed record Member(IPropertySymbol Property, string WireName, JsonWriterSkip Skip);

    /// <summary>The members of <paramref name="type" />, or the reason the writer cannot reproduce them.</summary>
    public static ImmutableArray<Member>? Read(INamedTypeSymbol type, out string? rejection)
    {
        rejection = RefusedOnTheType(type);
        if (rejection is not null)
            return null;

        var members = new List<(Member Member, int Order, int Index)>();
        var index = 0;

        foreach (var property in PropertyAnalyzer.GetAllProperties(type))
        {
            if (property.IsIndexer)
                continue;

            if (Attribute(property, "JsonConverterAttribute") is not null)
                return Refuse(out rejection, $"{property.Name} declares a [JsonConverter]");
            if (Attribute(property, "JsonExtensionDataAttribute") is not null)
                return Refuse(out rejection, $"{property.Name} is [JsonExtensionData]");
            if (Attribute(property, "JsonNumberHandlingAttribute") is not null)
                return Refuse(out rejection, $"{property.Name} declares [JsonNumberHandling]");

            var ignore = Attribute(property, "JsonIgnoreAttribute");
            var condition = ignore is null ? (int?)null : IgnoreCondition(ignore);
            if (condition == IgnoreAlways)
                continue;

            // A non-public getter is not read, unless [JsonInclude] says so: then it is, and the writer, which
            // reads through the public surface, cannot.
            if (property.GetMethod is not { DeclaredAccessibility: Accessibility.Public })
            {
                if (Attribute(property, "JsonIncludeAttribute") is not null)
                    return Refuse(out rejection, $"{property.Name} is [JsonInclude] without a public getter");
                continue;
            }

            if (property.Type.IsRefLikeType || property.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
                return Refuse(out rejection, $"{property.Name} has a type System.Text.Json cannot write");

            var skip = condition switch
            {
                IgnoreNever => JsonWriterSkip.Never,
                IgnoreWhenWritingDefault => JsonWriterSkip.WhenDefault,
                // [JsonIgnore(Condition = WhenWritingNull)] and the host's DefaultIgnoreCondition are one rule,
                // and it applies only where a null can occur.
                _ => CanBeNull(property.Type) ? JsonWriterSkip.WhenNull : JsonWriterSkip.Never,
            };

            var order = FirstArgument(Attribute(property, "JsonPropertyOrderAttribute")) is int value ? value : 0;

            members.Add((new Member(property, WireName(property), skip), order, index++));
        }

        if (IncludedOutsideThePublicSurface(type) is { } included)
            return Refuse(out rejection, $"{included} is [JsonInclude] outside the public surface");

        var duplicate = members.GroupBy(m => m.Member.WireName, System.StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            return Refuse(out rejection, $"two members are written as \"{duplicate.Key}\"");

        // The resolver sorts by order, stably: equal orders keep the declaration order.
        return members.OrderBy(m => m.Order).ThenBy(m => m.Index).Select(m => m.Member).ToImmutableArray();
    }

    /// <summary><c>JsonNamingPolicy.CamelCase</c>, reproduced (<see cref="JsonWireNames.CamelCase" />).</summary>
    public static string CamelCase(string name) => JsonWireNames.CamelCase(name);

    /// <summary>Whether a value of the type can be null at runtime: a reference type, or a <c>Nullable&lt;T&gt;</c>.</summary>
    public static bool CanBeNull(ITypeSymbol type)
        => !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>What on the type itself keeps the writer from reproducing it, or null.</summary>
    public static string? RefusedOnTheType(INamedTypeSymbol type)
    {
        if (Attribute(type, "JsonConverterAttribute") is not null)
            return $"{type.Name} declares a [JsonConverter]";
        if (Attribute(type, "JsonPolymorphicAttribute") is not null || Attribute(type, "JsonDerivedTypeAttribute") is not null)
            return $"{type.Name} is polymorphic";
        if (Attribute(type, "JsonNumberHandlingAttribute") is not null)
            return $"{type.Name} declares [JsonNumberHandling]";
        return null;
    }

    private static ImmutableArray<Member>? Refuse(out string? rejection, string reason)
    {
        rejection = reason;
        return null;
    }

    private static string WireName(IPropertySymbol property)
        => FirstArgument(Attribute(property, "JsonPropertyNameAttribute")) is string name
            ? name
            : CamelCase(property.Name);

    private static object? FirstArgument(AttributeData? attribute)
        => attribute is { ConstructorArguments.Length: > 0 } ? attribute.ConstructorArguments[0].Value : null;

    /// <summary>A field, or a property that is not public, that <c>[JsonInclude]</c> puts on the wire.</summary>
    private static string? IncludedOutsideThePublicSurface(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
            {
                var outside = member switch
                {
                    IFieldSymbol { IsStatic: false } => true,
                    IPropertySymbol { IsStatic: false } property => property.DeclaredAccessibility != Accessibility.Public,
                    _ => false,
                };

                if (outside && Attribute(member, "JsonIncludeAttribute") is not null)
                    return member.Name;
            }

        return null;
    }

    private const int IgnoreNever = 0;
    private const int IgnoreAlways = 1;
    private const int IgnoreWhenWritingDefault = 2;

    /// <summary>The <c>JsonIgnoreCondition</c> an attribute carries: <c>Always</c> when it names none.</summary>
    private static int IgnoreCondition(AttributeData ignore)
    {
        foreach (var argument in ignore.NamedArguments)
            if (argument is { Key: "Condition", Value.Value: int condition })
                return condition;

        return IgnoreAlways;
    }

    private static AttributeData? Attribute(ISymbol symbol, string name)
        => symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == name
            && a.AttributeClass.ContainingNamespace?.ToDisplayString() == JsonNamespace);
}
