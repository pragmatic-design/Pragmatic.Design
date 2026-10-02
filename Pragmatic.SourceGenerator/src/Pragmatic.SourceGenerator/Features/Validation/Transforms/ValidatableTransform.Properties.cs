using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Property extraction methods for ValidatableTransform.
/// </summary>
internal static partial class ValidatableTransform
{
    public static ImmutableArray<PropertyValidationModel> ExtractValidatedProperties(
        INamedTypeSymbol typeSymbol,
        Compilation compilation,
        CancellationToken ct,
        bool isEntity = false)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyValidationModel>();
        var validationAttributeBase = compilation.GetTypeByMetadataName(ValidationAttributeFullName);
        var validateElementsAttr = compilation.GetTypeByMetadataName(ValidateElementsAttributeFullName);
        var iSyncValidator = compilation.GetTypeByMetadataName(ISyncValidatorFullName);

        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property)
                continue;

            var attributes = ExtractValidationAttributes(property, validationAttributeBase, ct);
            var hasValidateElementsAttr = HasAttribute(property, validateElementsAttr);

            var (isCollection, _, elementIsValidatable) = AnalyzeCollectionType(property.Type, iSyncValidator);
            var hasValidatableElements = !isEntity && isCollection && elementIsValidatable;
            var isNestedValidatable = !isEntity && !isCollection
                && AnalyzeObjectValidatable(property.Type, iSyncValidator);
            var isRequiredModifier = property.IsRequired;

            if (attributes.IsDefaultOrEmpty && !hasValidateElementsAttr
                && !hasValidatableElements && !isNestedValidatable && !isRequiredModifier)
                continue;

            var propModel = CreatePropertyModel(property, attributes, hasValidateElementsAttr, validateElementsAttr,
                iSyncValidator, isNestedValidatable, isRequiredModifier);
            builder.Add(propModel);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ValidationAttributeModel> ExtractValidationAttributes(
        IPropertySymbol property,
        INamedTypeSymbol? validationAttributeBase,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<ValidationAttributeModel>();

        foreach (var attr in property.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();
            if (attr.AttributeClass is null)
                continue;

            var attrFullName = attr.AttributeClass.ToDisplayString();
            var isValidationAttr = KnownValidationAttributes.Contains(attrFullName)
                || (validationAttributeBase is not null &&
                    InheritsFrom(attr.AttributeClass, validationAttributeBase));

            if (!isValidationAttr)
                continue;

            var model = CreateAttributeModel(attr);
            if (model is not null)
                builder.Add(model);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Public overload that resolves the validation attribute base type from Compilation.
    ///     Used by BodyDtoValidationTransform for cross-SG body DTO validation.
    /// </summary>
    internal static ImmutableArray<ValidationAttributeModel> ExtractPropertyValidationAttributes(
        IPropertySymbol property,
        Compilation compilation,
        CancellationToken ct)
    {
        var validationAttributeBase = compilation.GetTypeByMetadataName(
            "Pragmatic.Validation.Attributes.ValidationAttribute");
        return ExtractValidationAttributes(property, validationAttributeBase, ct);
    }

    private static PropertyValidationModel CreatePropertyModel(
        IPropertySymbol property,
        ImmutableArray<ValidationAttributeModel> attributes,
        bool hasValidateElements,
        INamedTypeSymbol? validateElementsAttr,
        INamedTypeSymbol? iSyncValidator,
        bool isNestedValidatable = false,
        bool isRequiredModifier = false)
    {
        var propertyType = property.Type;
        var isNullableValueType =
            propertyType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var isNullable = propertyType.NullableAnnotation == NullableAnnotation.Annotated
            || isNullableValueType;

        // A non-nullable value type (DateTime, int, Guid, enum, ...) can never be null,
        // so an `is null` / `== null` guard would be invalid C# (CS0037).
        var isNonNullableValueType = propertyType.IsValueType && !isNullableValueType;

        var isString = propertyType.SpecialType == SpecialType.System_String;
        var isNumeric = IsNumericType(propertyType);
        var isComparable = IsComparable(propertyType);
        var (isCollection, elementType, elementIsValidatable) = AnalyzeCollectionType(propertyType, iSyncValidator);
        var shouldValidateElements = hasValidateElements || (isCollection && elementIsValidatable);

        var stopOnFirst = false;
        if (hasValidateElements && validateElementsAttr is not null)
        {
            var attr = property.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, validateElementsAttr));
            if (attr is not null)
                stopOnFirst = GetNamedArgument<bool>(attr, "StopOnFirstError");
        }

        return new PropertyValidationModel
        {
            PropertyName = property.Name,
            WireName = Core.WireNameReader.Read(property),
            PropertyType = propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsNullable = isNullable,
            IsNonNullableValueType = isNonNullableValueType,
            IsString = isString,
            IsEnum = IsEnumType(propertyType),
            IsNumeric = isNumeric,
            IsComparable = isComparable,
            IsCollection = isCollection,
            ElementType = elementType,
            ElementIsValidatable = elementIsValidatable,
            Attributes = attributes,
            ValidatesElements = shouldValidateElements,
            DeclaresValidateElements = hasValidateElements,
            ValidateElementsStopOnFirst = stopOnFirst,
            IsNestedValidatable = isNestedValidatable,
            IsRequiredModifier = isRequiredModifier
        };
    }

    /// <summary>Whether the type — unwrapped of its nullability — is an enum.</summary>
    internal static bool IsEnumType(ITypeSymbol type)
    {
        var unwrapped = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

        return unwrapped.TypeKind == TypeKind.Enum;
    }
}
