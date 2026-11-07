using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator.Features.Messaging.Models;

namespace Pragmatic.SourceGenerator.Features.Messaging.Transforms;

/// <summary>
///     Reads how a <c>[CompensateWith&lt;T&gt;]</c> action is seeded from its saga (#532), with the rules of the JSON round trip the orchestrator
///     used to make, so the same compensator comes out without reflection:
///     <list type="bullet">
///         <item>the saga gives its public readable properties, but those marked <c>[JsonIgnore]</c>;</item>
///         <item>the constructor is the one marked <c>[JsonConstructor]</c>, else the public parameterless one, else the only public one;</item>
///         <item>a parameter or a public settable member takes the saga property of its name, ignoring case, when the value converts implicitly;</item>
///         <item>a member a parameter already set is not set again; a required member with no value is set to its default.</item>
///     </list>
/// </summary>
internal static class CompensationSeedTransform
{
    public static CompensationSeedModel Read(INamedTypeSymbol saga, ITypeSymbol compensator, Compilation compilation)
    {
        if (compensator is not INamedTypeSymbol type || type.IsAbstract || type.TypeKind is not (TypeKind.Class or TypeKind.Struct)
            || Constructor(type) is not { } constructor)
            return new CompensationSeedModel { CanConstruct = false };

        var source = SagaProperties(saga);
        string? From(string name, ITypeSymbol target)
            => source.TryGetValue(name, out var property) && compilation.HasImplicitConversion(property.Type, target)
                ? $"instance.{Escape(property.Name)}{(property.Type.IsReferenceType ? "!" : "")}"
                : null;

        var arguments = new List<string>();
        foreach (var parameter in constructor.Parameters)
        {
            if (From(parameter.Name, parameter.Type) is { } value)
                arguments.Add($"{Escape(parameter.Name)}: {value}");
            else if (!parameter.HasExplicitDefaultValue && !parameter.IsParams)
                arguments.Add($"{Escape(parameter.Name)}: default!");
        }

        var bound = new HashSet<string>(constructor.Parameters.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        var setsRequired = constructor.GetAttributes().Any(a => a.AttributeClass?.Name == "SetsRequiredMembersAttribute");
        var assignments = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = type; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                var (name, memberType, required, settable, ignored) = member switch
                {
                    IPropertySymbol { IsStatic: false, IsIndexer: false } p => (p.Name, p.Type, p.IsRequired, p.SetMethod is { DeclaredAccessibility: Accessibility.Public }, IsIgnored(p)),
                    IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } f => (f.Name, f.Type, f.IsRequired, false, true), // fields are not serialized
                    _ => (null, null, false, false, false),
                };
                if (name is null || memberType is null || member.DeclaredAccessibility != Accessibility.Public || !seen.Add(name))
                    continue;
                var mustSet = required && !setsRequired;
                if (!mustSet && (!settable || ignored || bound.Contains(name)))
                    continue;
                if ((settable && !ignored ? From(name, memberType) : null) is { } value)
                    assignments.Add($"{Escape(name)} = {value}");
                else if (mustSet)
                    assignments.Add($"{Escape(name)} = default!");
            }
        }

        return new CompensationSeedModel { CanConstruct = true, Arguments = arguments.ToImmutableArray(), Assignments = assignments.ToImmutableArray() };
    }

    /// <summary>The constructor System.Text.Json would call: <c>[JsonConstructor]</c>, the public parameterless one, or the only public one.</summary>
    private static IMethodSymbol? Constructor(INamedTypeSymbol type)
    {
        var constructors = type.InstanceConstructors.Where(c => !IsCopyConstructor(type, c)).ToList();
        if (constructors.FirstOrDefault(c => c.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonConstructorAttribute")) is { } marked)
            return marked;
        var visible = constructors.Where(c => c.DeclaredAccessibility == Accessibility.Public).ToList();
        return visible.FirstOrDefault(c => c.Parameters.Length == 0) ?? (visible.Count == 1 ? visible[0] : null);
    }

    /// <summary>A record's copy constructor, which no deserializer calls.</summary>
    private static bool IsCopyConstructor(INamedTypeSymbol type, IMethodSymbol constructor)
        => type.IsRecord && constructor.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type);

    /// <summary>The saga's public readable properties, by name ignoring case, the most derived declaration first.</summary>
    private static Dictionary<string, IPropertySymbol> SagaProperties(INamedTypeSymbol saga)
    {
        var properties = new Dictionary<string, IPropertySymbol>(StringComparer.OrdinalIgnoreCase);
        for (var current = saga; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property is { IsStatic: false, IsIndexer: false, DeclaredAccessibility: Accessibility.Public, GetMethod.DeclaredAccessibility: Accessibility.Public }
                    && !IsIgnored(property) && !properties.ContainsKey(property.Name))
                    properties[property.Name] = property;
            }
        }

        return properties;
    }

    private static bool IsIgnored(IPropertySymbol property) => property.GetAttributes().Any(a => a.AttributeClass?.Name == "JsonIgnoreAttribute");

    private static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
