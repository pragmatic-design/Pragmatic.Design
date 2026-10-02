using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.Documents.Csv.Generator.Models;

namespace Pragmatic.Documents.Csv.Generator.Transforms;

/// <summary>Extracts CsvTypeModel from a type symbol annotated with [CsvSerializable].</summary>
internal static class CsvTransform
{
    private const string CsvColumnAttributeFqn = "Pragmatic.Documents.Csv.CsvColumnAttribute";

    internal static CsvTypeModel? Extract(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var typeKeyword = symbol.IsRecord
            ? (symbol.IsValueType ? "record struct" : "record")
            : (symbol.IsValueType ? "struct" : "class");

        var accessibility = symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "public"
        };

        var properties = ExtractProperties(symbol);
        if (properties.Length == 0) return null;

        return new CsvTypeModel(ns, symbol.Name, typeKeyword, accessibility, properties);
    }

    private static ImmutableArray<CsvPropertyModel> ExtractProperties(INamedTypeSymbol symbol)
    {
        // Two-pass: first collect explicit orders, then assign implicit orders avoiding collisions.
        var members = new List<(IPropertySymbol Prop, string? Header, string? Format, int ExplicitOrder, bool Ignore)>();
        var explicitOrders = new HashSet<int>();

        foreach (var member in symbol.GetMembers())
        {
            if (member is not IPropertySymbol { DeclaredAccessibility: Accessibility.Public, IsStatic: false } prop)
                continue;
            if (prop.GetMethod is null) continue;

            var (header, format, explicitOrder, ignore) = GetColumnAttribute(prop);
            if (ignore) continue;

            members.Add((prop, header, format, explicitOrder, ignore));
            if (explicitOrder >= 0)
                explicitOrders.Add(explicitOrder);
        }

        var builder = ImmutableArray.CreateBuilder<CsvPropertyModel>(members.Count);
        var implicitCounter = 0;

        foreach (var (prop, header, format, explicitOrder, _) in members)
        {
            int actualOrder;
            if (explicitOrder >= 0)
            {
                actualOrder = explicitOrder;
            }
            else
            {
                // Advance counter past any reserved explicit-order slots.
                while (explicitOrders.Contains(implicitCounter))
                    implicitCounter++;
                actualOrder = implicitCounter++;
            }

            var (kind, isNullable) = ClassifyType(prop.Type);
            var typeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            builder.Add(new CsvPropertyModel(
                prop.Name,
                typeName,
                header ?? prop.Name,
                format,
                actualOrder,
                isNullable,
                kind,
                IsWritable: prop.SetMethod is not null));
        }

        builder.Sort((a, b) => a.Order.CompareTo(b.Order));
        return builder.ToImmutable();
    }

    private static (string? Header, string? Format, int Order, bool Ignore) GetColumnAttribute(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != CsvColumnAttributeFqn) continue;

            string? header = null;
            string? format = null;
            var order = -1;
            var ignore = false;

            // Constructor argument: header
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string h)
                header = h;

            // Named arguments
            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Format" when named.Value.Value is string f:
                        format = f;
                        break;
                    case "Order" when named.Value.Value is int o:
                        order = o;
                        break;
                    case "Ignore" when named.Value.Value is bool ig:
                        ignore = ig;
                        break;
                    case "Header" when named.Value.Value is string hdr:
                        header = hdr;
                        break;
                }
            }

            return (header, format, order, ignore);
        }

        return (null, null, -1, false);
    }

    private static (CsvPropertyKind Kind, bool IsNullable) ClassifyType(ITypeSymbol type)
    {
        var isNullable = false;

        // Unwrap Nullable<T>
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedType)
        {
            type = namedType.TypeArguments[0];
            isNullable = true;
        }
        // Nullable reference type
        else if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol)
        {
            isNullable = true;
        }

        var kind = type.SpecialType switch
        {
            SpecialType.System_String => CsvPropertyKind.String,
            SpecialType.System_Int32 => CsvPropertyKind.Int,
            SpecialType.System_Int64 => CsvPropertyKind.Long,
            SpecialType.System_Double => CsvPropertyKind.Double,
            SpecialType.System_Single => CsvPropertyKind.Float,
            SpecialType.System_Decimal => CsvPropertyKind.Decimal,
            SpecialType.System_Boolean => CsvPropertyKind.Bool,
            SpecialType.System_DateTime => CsvPropertyKind.DateTime,
            _ when type.TypeKind == TypeKind.Enum => CsvPropertyKind.Enum,
            _ => type.ToDisplayString() switch
            {
                "System.DateTimeOffset" => CsvPropertyKind.DateTimeOffset,
                "System.Guid" => CsvPropertyKind.Guid,
                "System.TimeSpan" => CsvPropertyKind.TimeSpan,
                _ => CsvPropertyKind.Other
            }
        };

        if (type.SpecialType == SpecialType.System_String)
            isNullable = true; // strings are always nullable-safe

        return (kind, isNullable);
    }
}
