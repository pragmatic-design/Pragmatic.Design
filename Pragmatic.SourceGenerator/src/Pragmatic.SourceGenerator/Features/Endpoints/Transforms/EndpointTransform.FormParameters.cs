using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Claim and form/file parameter parsing (FromClaim, FromForm, IFormFile, MaxFileSize, AllowedContentTypes).
/// </summary>
internal static partial class EndpointTransform
{
    private static ImmutableArray<ClaimParameterModel> ParseClaimParameters(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p =>
            {
                var claimAttr = p.GetAttributes()
                    .FirstOrDefault(a => IsFromClaimAttribute(a));

                if (claimAttr is null)
                    return null;

                // Get claim type from constructor argument
                var claimType = claimAttr.ConstructorArguments.Length > 0
                    ? claimAttr.ConstructorArguments[0].Value?.ToString() ?? p.Name
                    : p.Name;

                // Get IsRequired from named arguments (defaults to true)
                var isRequired = true;
                foreach (var namedArg in claimAttr.NamedArguments)
                    if (namedArg.Key == "IsRequired")
                        isRequired = (bool)(namedArg.Value.Value ?? true);

                var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var needsConversion = typeName != "string" && typeName != "string?";

                return new ClaimParameterModel
                {
                    ClaimType = claimType,
                    PropertyName = p.Name,
                    TypeName = typeName,
                    IsRequired = isRequired,
                    NeedsConversion = needsConversion,
                    IsInitOnly = p.SetMethod?.IsInitOnly == true,
                    InitOnlyFallback = GetInitOnlyFallback(p)
                };
            })
            .Where(c => c is not null)
            .Select(c => c!)
            .ToImmutableArray();
    }

    private static ImmutableArray<FormParameterModel> ParseFormParameters(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p =>
            {
                var formAttr = p.GetAttributes()
                    .FirstOrDefault(a => IsFromFormAttribute(a));

                if (formAttr is null)
                    return null;

                // Get form field name from Name property or use property name
                var fieldName = p.Name;
                foreach (var namedArg in formAttr.NamedArguments)
                    if (namedArg.Key == "Name")
                        fieldName = namedArg.Value.Value?.ToString() ?? p.Name;

                var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isFile = typeName.Contains("IFormFile");

                // Parse file validation attributes
                long? maxFileSize = null;
                var allowedContentTypes = System.Collections.Immutable.ImmutableArray<string>.Empty;

                if (isFile)
                {
                    var maxSizeAttr = p.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.MaxFileSize);
                    if (maxSizeAttr?.ConstructorArguments.Length > 0 && maxSizeAttr.ConstructorArguments[0].Value is long maxBytes)
                        maxFileSize = maxBytes;

                    var contentTypesAttr = p.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.AllowedContentTypes);
                    if (contentTypesAttr?.ConstructorArguments.Length > 0 &&
                        contentTypesAttr.ConstructorArguments[0].Kind == TypedConstantKind.Array)
                    {
                        allowedContentTypes = contentTypesAttr.ConstructorArguments[0].Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!)
                            .ToImmutableArray();
                    }
                }

                return new FormParameterModel
                {
                    Name = fieldName,
                    PropertyName = p.Name,
                    TypeName = typeName,
                    IsRequired = p.IsRequired,
                    IsRequiredByValidation = CarriesValidationRequired(p),
                    IsNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                                 || p.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T },
                    IsFile = isFile,
                    MaxFileSize = maxFileSize,
                    AllowedContentTypes = allowedContentTypes
                };
            })
            .Where(f => f is not null)
            .Select(f => f!)
            .ToImmutableArray();
    }
}
