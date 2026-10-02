using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for attribute extraction.
/// </summary>
internal static class AttributeAnalyzer
{
    /// <summary>
    ///     The attributes a mapping reads for a DTO property: its own and, on a positional record, those
    ///     written on the primary-constructor parameter the property is declared from.
    /// </summary>
    /// <remarks>
    ///     An attribute written on a record's parameter stays on the parameter — the property the compiler
    ///     synthesizes from it carries none — and <c>[MapProperty]</c>, <c>[MapIgnore]</c> and
    ///     <c>[MapConverter]</c> declare <c>AttributeTargets.Parameter</c> to be written exactly there.
    ///     Read from the property alone, they compiled on a parameter and did nothing.
    /// </remarks>
    private static IEnumerable<AttributeData> MappingAttributesOf(IPropertySymbol prop)
    {
        var own = prop.GetAttributes();
        return PositionalParameterOf(prop) is { } parameter ? own.Concat(parameter.GetAttributes()) : own;
    }

    /// <summary>The primary-constructor parameter a positional record's property is declared from.</summary>
    private static IParameterSymbol? PositionalParameterOf(IPropertySymbol prop)
    {
        if (prop.ContainingType is not { IsRecord: true } record)
            return null;

        foreach (var constructor in record.InstanceConstructors)
        {
            // The primary constructor is the one the record's own header declares; the copy constructor
            // and any written in the body are not.
            if (!constructor.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is RecordDeclarationSyntax))
                continue;

            return constructor.Parameters.FirstOrDefault(p => p.Name == prop.Name);
        }

        return null;
    }

    /// <summary>
    ///     Checks if a property has a specific attribute.
    /// </summary>
    public static bool HasAttribute(IPropertySymbol prop, string attributeName)
    {
        return prop.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName);
    }

    /// <summary>
    ///     Whether <c>[MapIgnore]</c> excludes the property in the direction being generated.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Five places read this attribute — the read side, the projection, the write side, a
    ///         mutation's auto-map and a patch — and each knows which way it is going. Asking here
    ///         keeps the positional contract of the enum in one place.
    ///     </para>
    ///     <para>
    ///         The constructor argument arrives as the underlying <c>int</c>: <c>0</c> is
    ///         <c>Both</c>, <c>1</c> <c>FromEntity</c>, <c>2</c> <c>ToEntity</c>. No argument means
    ///         the parameterless constructor, which is <c>Both</c> — what the attribute meant before
    ///         it had a direction, so nothing that already used it changes.
    ///     </para>
    /// </remarks>
    /// <param name="prop">The DTO property.</param>
    /// <param name="writing">True on a write path, false on a read path.</param>
    public static bool IsIgnoredFor(IPropertySymbol prop, bool writing)
    {
        foreach (var attribute in MappingAttributesOf(prop))
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapIgnoreAttributeName)
                continue;

            var direction = attribute.ConstructorArguments.Length == 1
                            && attribute.ConstructorArguments[0].Value is int value
                ? value
                : 0;

            return direction switch
            {
                1 => !writing,
                2 => writing,
                _ => true,
            };
        }

        return false;
    }

    private const string MapIgnoreAttributeName = "Pragmatic.Mapping.Attributes.MapIgnoreAttribute";

    /// <summary>
    ///     <c>[MapEnum(OnUnknown = …)]</c> on the property, or <c>Throw</c>.
    /// </summary>
    /// <remarks>
    ///     Positional, like the other enum arguments read from symbols: <c>0</c> is <c>Throw</c>,
    ///     <c>1</c> <c>Default</c>, <c>2</c> <c>Null</c>. It follows <c>UnknownEnumValue</c>'s
    ///     declaration order, and that contract lives here alone.
    /// </remarks>
    public static string ReadEnumOnUnknown(IPropertySymbol prop)
    {
        foreach (var attribute in prop.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapEnumAttributeName)
                continue;

            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key != "OnUnknown" || named.Value.Value is not int value)
                    continue;

                return value switch
                {
                    1 => "Default",
                    2 => "Null",
                    _ => "Throw",
                };
            }
        }

        return "Throw";
    }

    /// <summary>
    ///     <c>[MapEnum(Match = EnumMatch.ByValue)]</c> on the property.
    /// </summary>
    public static bool MatchesEnumsByValue(IPropertySymbol prop)
    {
        foreach (var attribute in prop.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != MapEnumAttributeName)
                continue;

            // A constructor argument: `Match` is taken by Attribute itself, so the choice travels
            // positionally — 0 is ByName, 1 is ByValue.
            if (attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is 1)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The wire names of an enum's members, for the ones that declare an alias.
    /// </summary>
    /// <remarks>
    ///     Returned as <c>Member=alias</c> pairs so the model stays a value: an array of strings
    ///     compares by content, which is what the incremental pipeline needs, and a tuple array would
    ///     not without a comparer of its own.
    /// </remarks>
    public static ImmutableArray<string> ReadEnumAliases(ITypeSymbol? enumType)
    {
        if (enumType is not INamedTypeSymbol { TypeKind: TypeKind.Enum } named)
            return ImmutableArray<string>.Empty;

        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var member in named.GetMembers().OfType<IFieldSymbol>())
        {
            if (!member.IsStatic)
                continue;

            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != MapEnumAttributeName)
                    continue;

                foreach (var argument in attribute.NamedArguments)
                    if (argument.Key == "Alias" && argument.Value.Value is string alias && alias.Length > 0)
                        builder.Add(member.Name + "=" + alias);
            }
        }

        return builder.ToImmutable();
    }

    private const string MapEnumAttributeName = "Pragmatic.Mapping.Attributes.MapEnumAttribute";

    /// <summary>
    ///     Gets the [MapProperty] attribute info from a property.
    /// </summary>
    public static MapPropertyInfo? GetMapPropertyAttribute(IPropertySymbol prop)
    {
        var attr = MappingAttributesOf(prop).FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "Pragmatic.Mapping.Attributes.MapPropertyAttribute");

        if (attr is null)
            return null;

        var sourcePaths = ImmutableArray<string>.Empty;
        string? format = null;
        var separator = " ";
        string? defaultValue = null;
        string? target = null;

        // Constructor arguments (source paths)
        if (attr.ConstructorArguments.Length > 0)
        {
            var firstArg = attr.ConstructorArguments[0];
            if (firstArg.Kind == TypedConstantKind.Array)
                sourcePaths = firstArg.Values
                    .Where(v => v.Value is string)
                    .Select(v => (string)v.Value!)
                    .ToImmutableArray();
            else if (firstArg.Value is string singlePath)
                sourcePaths = ImmutableArray.Create(singlePath);
        }

        // Named arguments
        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Format":
                    format = namedArg.Value.Value as string;
                    break;
                case "Separator":
                    separator = namedArg.Value.Value as string ?? " ";
                    break;
                case "Default":
                    defaultValue = namedArg.Value.Value?.ToString();
                    break;
                case "Target":
                    target = namedArg.Value.Value as string;
                    break;
            }

        return new MapPropertyInfo(sourcePaths, format, separator, defaultValue, target);
    }

    /// <summary>
    ///     Gets the converter type from [MapConverter] attribute.
    /// </summary>
    public static string? GetConverterAttribute(IPropertySymbol prop)
        => GetConverterInfo(prop).TypeName;

    /// <summary>
    ///     Gets the converter type from [MapConverter] plus its contract validity: it must implement
    ///     <c>IValueConverter&lt;,&gt;</c> (PRAG0305) and expose a public parameterless constructor
    ///     (PRAG0306) — the generated code instantiates it with <c>new()</c>.
    /// </summary>
    public static ConverterInfo GetConverterInfo(IPropertySymbol prop)
    {
        var attr = MappingAttributesOf(prop).FirstOrDefault(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Mapping.Attributes.MapConverterAttribute") == true);

        if (attr?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrType
            || attrType.TypeArguments[0] is not INamedTypeSymbol converter)
            return new ConverterInfo(null, false, false);

        var missingInterface = !converter.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "Pragmatic.Mapping.Converters.IValueConverter<TSource, TTarget>");

        var missingCtor = !converter.IsAbstract
            && !converter.InstanceConstructors.Any(c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        return new ConverterInfo(converter.ToDisplayString(), missingInterface, missingCtor);
    }

    /// <summary>
    ///     Class-level [MapConverter&lt;T&gt;]: finds the first converter on the containing type whose
    ///     <c>IValueConverter&lt;TSource, TTarget&gt;</c> signature matches the property's
    ///     source→target type pair (compared with nullable annotations stripped). Property-level
    ///     converters take precedence — call this only when none is present.
    /// </summary>
    public static ConverterInfo GetTypeLevelConverterInfo(
        INamedTypeSymbol containingType,
        string? sourceTypeName,
        string targetTypeName)
    {
        if (sourceTypeName is null)
            return new ConverterInfo(null, false, false);

        var src = sourceTypeName.TrimEnd('?');
        var tgt = targetTypeName.TrimEnd('?');

        foreach (var attr in containingType.GetAttributes())
        {
            if (attr.AttributeClass?.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Mapping.Attributes.MapConverterAttribute") != true
                || attr.AttributeClass is not { TypeArguments.Length: 1 } attrType
                || attrType.TypeArguments[0] is not INamedTypeSymbol converter)
                continue;

            var iface = converter.AllInterfaces.FirstOrDefault(i =>
                i.OriginalDefinition.ToDisplayString() == "Pragmatic.Mapping.Converters.IValueConverter<TSource, TTarget>");
            if (iface is not { TypeArguments.Length: 2 })
                continue;

            if (iface.TypeArguments[0].ToDisplayString().TrimEnd('?') != src
                || iface.TypeArguments[1].ToDisplayString().TrimEnd('?') != tgt)
                continue;

            var missingCtor = !converter.IsAbstract
                && !converter.InstanceConstructors.Any(c =>
                    c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
            return new ConverterInfo(converter.ToDisplayString(), MissingInterface: false, missingCtor);
        }

        return new ConverterInfo(null, false, false);
    }
}

/// <summary>
///     Converter type extracted from [MapConverter], with contract-validity flags.
/// </summary>
internal sealed record ConverterInfo(string? TypeName, bool MissingInterface, bool MissingCtor);

/// <summary>
///     Info extracted from [MapProperty] attribute.
/// </summary>
internal sealed record MapPropertyInfo(
    ImmutableArray<string> SourcePaths,
    string? Format,
    string Separator,
    string? DefaultValue,
    string? Target);
