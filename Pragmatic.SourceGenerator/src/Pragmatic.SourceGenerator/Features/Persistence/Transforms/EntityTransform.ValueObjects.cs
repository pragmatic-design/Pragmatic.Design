using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Value-object flattening: an entity property whose type is a <c>[ValueObject]</c> is mapped as an
///     EF Core complex type, flattened into <c>{Property}_{SubProperty}</c> columns shared by the entity
///     configuration and the migration schema.
/// </summary>
internal static partial class EntityTransform
{
    private const string ValueObjectAttributeFqn = "Pragmatic.Persistence.Entity.ValueObjectAttribute";

    /// <summary>
    ///     Returns the flattened complex-type columns for a value-object property, or empty when the
    ///     property's type is not a <c>[ValueObject]</c>. Only scalar/enum sub-properties are mapped.
    /// </summary>
    private const string MoneyTypeFqn = "Pragmatic.Internationalization.Types.Money";

    private static EquatableArray<ValueObjectColumnModel> ComputeValueObjectColumns(IPropertySymbol prop)
    {
        var type = Unwrap(prop.Type);
        if (type is not INamedTypeSymbol named)
            return EquatableArray<ValueObjectColumnModel>.Empty;

        // Money carries no [ValueObject] attribute — it is the framework's own type — and
        // EntityConfigurationTemplate.RenderMoneyComplexProperty maps it as a complex type all the same,
        // into {Property}_Amount and {Property}_Currency. Left out here, the schema kept mapping it to one
        // fallback column: EF wrote two columns the migration had never created, so every insert of an
        // entity holding an amount failed with a constraint violation. The two halves have to agree.
        if (named.ToDisplayString() == MoneyTypeFqn)
            return ImmutableArray.Create(
                new ValueObjectColumnModel
                {
                    ColumnName = $"{prop.Name}_Amount",
                    TypeName = "decimal",
                    IsNullable = IsNullableProperty(prop),
                    IsEnum = false
                },
                new ValueObjectColumnModel
                {
                    ColumnName = $"{prop.Name}_Currency",
                    TypeName = "string",
                    IsNullable = IsNullableProperty(prop),
                    IsEnum = false
                });

        var isValueObject = named.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == ValueObjectAttributeFqn);
        if (!isValueObject)
            return EquatableArray<ValueObjectColumnModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<ValueObjectColumnModel>();
        foreach (var sub in named.GetMembers().OfType<IPropertySymbol>())
        {
            if (sub.IsStatic || sub.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (!IsMappableScalar(sub.Type, out var isEnum))
                continue;

            var subNullable = sub.NullableAnnotation == NullableAnnotation.Annotated
                || sub.Type is { IsValueType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

            builder.Add(new ValueObjectColumnModel
            {
                ColumnName = $"{prop.Name}_{sub.Name}",
                TypeName = sub.Type.ToDisplayString(),
                IsNullable = subNullable,
                IsEnum = isEnum
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>Whether the property itself may be absent — both halves of a complex type follow it.</summary>
    private static bool IsNullableProperty(IPropertySymbol prop)
        => prop.NullableAnnotation == NullableAnnotation.Annotated
           || prop.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static ITypeSymbol Unwrap(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            && n.TypeArguments.Length == 1
            ? n.TypeArguments[0]
            : type;

    private static bool IsMappableScalar(ITypeSymbol type, out bool isEnum)
    {
        isEnum = false;
        var t = Unwrap(type);

        if (t.TypeKind == TypeKind.Enum)
        {
            isEnum = true;
            return true;
        }

        switch (t.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_DateTime:
                return true;
        }

        return t.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.TimeSpan"
            or "System.DateOnly" or "System.TimeOnly";
    }
}
