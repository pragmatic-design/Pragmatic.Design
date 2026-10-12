using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Configuration.Models;

namespace Pragmatic.SourceGenerator.Features.Configuration;

/// <summary>
///     Decides whether a <c>[Configuration]</c> class gets a generated <c>ITenantOptionsBinder&lt;T&gt;</c>, and
///     which properties it sets.
/// </summary>
/// <remarks>
///     The binder replaces <c>ConfigurationBinder.Bind</c> for <c>TenantOptions&lt;T&gt;</c>, so it is emitted
///     only where it binds exactly what the reflective binder would: every property with a public getter has a
///     public, non-init setter and a scalar type <c>ConfigurationValue</c> converts, and the class derives from
///     nothing but <c>object</c>. Anything else (a collection, a nested section, a get-only or inherited
///     property) keeps the reflective path, rather than a binder that silently skips a property.
/// </remarks>
internal static class TypedBindingTransform
{
    private const string KeyNameAttribute = "Microsoft.Extensions.Configuration.ConfigurationKeyNameAttribute";

    /// <returns>The properties to set, or default when the class must keep the reflective binder.</returns>
    public static ImmutableArray<TypedBindingModel> Bindings(INamedTypeSymbol symbol)
    {
        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object })
            return default;

        var bindings = ImmutableArray.CreateBuilder<TypedBindingModel>();
        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (property.SetMethod is not { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
                return default;

            var binding = Binding(property);
            if (binding is null)
                return default;
            bindings.Add(binding);
        }

        return bindings.ToImmutable();
    }

    private static TypedBindingModel? Binding(IPropertySymbol property)
    {
        var type = property.Type;
        var isNullable = false;
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
            isNullable = true;
        }

        string? converter;
        if (type.SpecialType == SpecialType.System_String)
            converter = null;
        else if (Converter(type) is { } known)
            converter = known;
        else
            return null;

        return new TypedBindingModel
        {
            PropertyName = property.Name,
            Key = KeyName(property) ?? property.Name,
            Converter = converter,
            IsNullable = isNullable,
        };
    }

    private static string? Converter(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
            return $"Enum<{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return "Boolean";
            case SpecialType.System_Byte: return "Byte";
            case SpecialType.System_SByte: return "SByte";
            case SpecialType.System_Int16: return "Int16";
            case SpecialType.System_UInt16: return "UInt16";
            case SpecialType.System_Int32: return "Int32";
            case SpecialType.System_UInt32: return "UInt32";
            case SpecialType.System_Int64: return "Int64";
            case SpecialType.System_UInt64: return "UInt64";
            case SpecialType.System_Single: return "Single";
            case SpecialType.System_Double: return "Double";
            case SpecialType.System_Decimal: return "Decimal";
            case SpecialType.System_DateTime: return "DateTime";
        }

        return type.ToDisplayString() switch
        {
            "System.Guid" => "Guid",
            "System.TimeSpan" => "TimeSpan",
            "System.DateTimeOffset" => "DateTimeOffset",
            _ => null,
        };
    }

    private static string? KeyName(IPropertySymbol property)
        => property.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == KeyNameAttribute && a.ConstructorArguments.Length == 1)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .FirstOrDefault(name => !string.IsNullOrEmpty(name));
}
