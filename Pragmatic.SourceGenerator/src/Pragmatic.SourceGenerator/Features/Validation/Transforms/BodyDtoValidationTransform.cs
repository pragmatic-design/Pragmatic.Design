using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Detects endpoint body DTOs that the Endpoints SG will generate,
///     and creates ValidatableModel for those body DTOs
///     so the Validation SG can generate ISyncValidator implementations.
/// </summary>
internal static class BodyDtoValidationTransform
{
    private const string EndpointAttributeFullName = "Pragmatic.Endpoints.Attributes.EndpointAttribute";

    public static ValidatableModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;
        if (context.TargetNode is not TypeDeclarationSyntax)
            return null;

        var route = GetRoute(context.Attributes);
        if (route is null)
            return null;

        var isDomainAction = IsDomainAction(symbol);
        var isMutation = IsMutation(symbol);
        var routeParamNames = ParseRouteParameterNames(route, symbol);
        var bodyProperties = ParseBodyProperties(symbol, routeParamNames);

        var hasSingleScalar = bodyProperties.Length == 1 && bodyProperties[0].IsScalar;
        // The flag has to be read here too: predicting an envelope the Endpoints SG will not generate
        // produces a validator for a type that does not exist.
        var bindBodyDirectly = GetBindBodyDirectly(context.Attributes);
        // The verb, for the same reason as the flag above and found the same way: a GET binds from the
        // query string and gets no body DTO, so predicting one here produces a validator for a type the
        // Endpoints SG will not generate. It compiled in this framework and broke an application built
        // on the packages, because nothing here has a GET carrying validated properties.
        var httpMethod = EndpointBodyDetection.GetHttpMethod(context.Attributes, EndpointAttributeFullName);
        // ⚠️ And whether it is a query, for the third time on the same rule. A [Query] gets no envelope
        // — its properties are filters and paging, and a canonical grid request binds directly — so a
        // validator predicted here is a partial record whose other half nobody writes: CS0103 on every
        // property it names, inside a generated file. Measured on an application declaring
        // `required GridFilterRequest` on a query; without `required` there was nothing to validate and
        // the disagreement stayed invisible.
        var isQuery = IsQuery(symbol);
        // ⚠️ And whether the request is multipart. An operation that carries a file has no JSON body
        // at all: its values travel as form fields, so a validator predicted here would name members
        // of a record the Endpoints feature does not emit.
        var carriesMultipart = CarriesMultipartRequest(symbol);
        if (!EndpointBodyDetection.NeedsBodyDto(
                isDomainAction, isMutation, bodyProperties.Length, hasSingleScalar, bindBodyDirectly,
                httpMethod, isQuery, carriesMultipart))
            return null;

        // Skip versioned actions — Endpoints SG generates V1Body/V2Body instead
        if (isDomainAction && HasActionVersioning(symbol, context.SemanticModel.Compilation))
            return null;

        var validatedProperties = ExtractValidatedBodyProperties(
            bodyProperties, symbol, context.SemanticModel.Compilation, ct);

        if (validatedProperties.IsDefaultOrEmpty)
            return null;

        var bodyDtoName = EndpointBodyDetection.GetBodyDtoName(symbol.Name);
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        return new ValidatableModel
        {
            Namespace = ns,
            TypeName = bodyDtoName,
            Accessibility = "public",
            TypeKind = "record",
            IsRecord = true,
            IsValueType = false,
            IsPartial = true,
            IsEntity = false,
            Properties = validatedProperties,
            AllPropertyNames = bodyProperties.Select(p => p.Name).ToImmutableArray(),
            AllPropertyTypes = bodyProperties.Select(p =>
                p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).ToImmutableArray(),
            PropertyDependencies = EquatableArray<Models.PropertyDependencyModel>.Empty
        };
    }

    /// <summary>Reads <c>BindBodyDirectly</c> from the [Endpoint] attribute.</summary>
    private static bool GetBindBodyDirectly(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        foreach (var named in attribute.NamedArguments)
            if (named.Key == "BindBodyDirectly" && named.Value.Value is true)
                return true;

        return false;
    }

    private static string? GetRoute(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attr in attributes)
        {
            if (attr.AttributeClass?.ToDisplayString() != EndpointAttributeFullName)
                continue;
            if (attr.ConstructorArguments.Length >= 2 && attr.ConstructorArguments[1].Value is string route)
                return route;
        }
        return null;
    }

    private static bool IsDomainAction(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();
            if (baseName.StartsWith("Pragmatic.Actions.Abstractions.DomainAction") ||
                baseName.StartsWith("Pragmatic.Actions.Abstractions.VoidDomainAction"))
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static bool IsMutation(INamedTypeSymbol symbol)
    {
        var baseType = symbol.BaseType;
        while (baseType is not null)
        {
            var baseName = baseType.OriginalDefinition.ToDisplayString();
            if (baseName.StartsWith("Pragmatic.Actions.Mutation.Mutation<"))
                return true;
            baseType = baseType.BaseType;
        }
        return false;
    }

    private static ImmutableHashSet<string> ParseRouteParameterNames(string route, INamedTypeSymbol symbol)
    {
        var properties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        var routeParamNames = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
        var regex = new Regex(@"\{(\w+)(?::(\w+))?\??}");
        foreach (Match match in regex.Matches(route))
        {
            var paramName = match.Groups[1].Value;
            if (properties.TryGetValue(paramName, out var prop))
                routeParamNames.Add(prop.Name);
        }
        return routeParamNames.ToImmutable();
    }

    private sealed class BodyPropertyInfo
    {
        public string Name { get; init; } = "";
        public ITypeSymbol Type { get; init; } = null!;
        public IPropertySymbol Symbol { get; init; } = null!;
        public bool IsScalar { get; init; }
    }

    private static ImmutableArray<BodyPropertyInfo> ParseBodyProperties(
        INamedTypeSymbol symbol,
        ImmutableHashSet<string> routeParamNames)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                p.SetMethod is not null &&
                !routeParamNames.Contains(p.Name) &&
                !IsServiceType(p.Type) &&
                !HasNonBodyBindingAttribute(p))
            .Select(p => new BodyPropertyInfo
            {
                Name = p.Name,
                Type = p.Type,
                Symbol = p,
                IsScalar = TypeAnalysis.IsScalarType(p.Type)
            })
            .ToImmutableArray();
    }

    private static ImmutableArray<PropertyValidationModel> ExtractValidatedBodyProperties(
        ImmutableArray<BodyPropertyInfo> bodyProperties,
        INamedTypeSymbol endpointSymbol,
        Compilation compilation,
        CancellationToken ct)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyValidationModel>();

        foreach (var bodyProp in bodyProperties)
        {
            var prop = bodyProp.Symbol;
            var isRequired = prop.IsRequired;
            var hasValidationAttrs = HasValidationAttributes(prop, compilation);

            if (!isRequired && !hasValidationAttrs)
                continue;

            var validationAttrs = hasValidationAttrs
                ? ValidatableTransform.ExtractPropertyValidationAttributes(prop, compilation, ct)
                : ImmutableArray<ValidationAttributeModel>.Empty;

            var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
            var isString = prop.Type.SpecialType == SpecialType.System_String;
            var isNumeric = ValidatableTransform.IsNumericType(prop.Type);
            var isComparable = ValidatableTransform.IsComparable(prop.Type);

            builder.Add(new PropertyValidationModel
            {
                PropertyName = prop.Name,
                PropertyType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsNullable = isNullable,
                IsString = isString,
                // The model is built in two places: here and in ValidatableTransform. A field added
                // to only one of them gives a false positive where the generated code goes through this one.
                IsEnum = ValidatableTransform.IsEnumType(prop.Type),
                IsNumeric = isNumeric,
                IsComparable = isComparable,
                // ⚠️ The three length rules read this to choose between Length and Count, so a constant
                // false would not be harmless. Same analysis as the other transform, which builds the
                // same model.
                IsCollection = ValidatableTransform.AnalyzeCollectionType(prop.Type, null).isCollection,
                ValidatesElements = false,
                DeclaresValidateElements = false,
                ElementIsValidatable = false,
                IsNestedValidatable = false,
                IsRequiredModifier = isRequired,
                Attributes = validationAttrs
            });
        }

        return builder.ToImmutable();
    }

    private static bool HasValidationAttributes(IPropertySymbol property, Compilation compilation)
    {
        var validationBase = compilation.GetTypeByMetadataName(
            "Pragmatic.Validation.Attributes.ValidationAttribute");
        if (validationBase is null)
            return false;

        foreach (var attr in property.GetAttributes())
        {
            var attrType = attr.AttributeClass;
            while (attrType is not null)
            {
                if (SymbolEqualityComparer.Default.Equals(attrType, validationBase))
                    return true;
                attrType = attrType.BaseType;
            }
        }
        return false;
    }

    private static bool IsServiceType(ITypeSymbol type) => Core.ServiceTypeDetector.IsServiceType(type);

    private static bool HasActionVersioning(INamedTypeSymbol symbol, Compilation compilation)
    {
        var hasVersionedMethods = symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Any(m => m.Name.StartsWith("ExecuteV") && m.Name.Length > 8 && char.IsDigit(m.Name[8]));
        if (!hasVersionedMethods)
            return false;
        return compilation.ReferencedAssemblyNames.Any(a => a.Name == "Asp.Versioning.Http");
    }

    /// <remarks>
    ///     The same list the Endpoints transform excludes from the body, and it has to stay the same
    ///     list: this is the second copy of "what is a body property", and every source missing here
    ///     produced a validator for a <c>…Body</c> type that names a member the Endpoints feature never
    ///     put there. ⚠️ <c>[FromForm]</c> was missing — a form operation has no body DTO at all, and
    ///     its validator was CS0103 on every rule it carried. Claim and cookie were missing with it.
    /// </remarks>
    /// <summary>Whether the type carries <c>[Query&lt;…&gt;]</c>, whatever its arity.</summary>
    private static bool IsQuery(INamedTypeSymbol symbol)
        => symbol.GetAttributes()
            .Any(a => a.AttributeClass?.Name.StartsWith("QueryAttribute", StringComparison.Ordinal) == true);

    /// <summary>
    ///     Whether the operation's request is <c>multipart/form-data</c>: a declared form field, or a
    ///     file, makes it so.
    /// </summary>
    /// <remarks>
    ///     The type of the property and not only the attribute, because a file is what makes the request
    ///     multipart — the <c>[HasAttachments]</c> upload declares no <c>[FromForm]</c> at all and is
    ///     still a form. Read here rather than taken from the endpoint model, like every other half of
    ///     this decision: this transform deliberately sees the symbol and not the model.
    /// </remarks>
    private static bool CarriesMultipartRequest(INamedTypeSymbol symbol)
        => symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Any(p => HasFormBindingAttribute(p) || IsFormFile(p.Type));

    private static bool HasFormBindingAttribute(IPropertySymbol property)
        => property.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString().Contains("FromForm") == true
            || a.AttributeClass?.Name is "FromFormAttribute" or "FromForm");

    private static bool IsFormFile(ITypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimEnd('?');
        return name.EndsWith("IFormFile", StringComparison.Ordinal)
               || name.EndsWith("IFormFileCollection", StringComparison.Ordinal);
    }

    private static bool HasNonBodyBindingAttribute(IPropertySymbol property)
    {
        return property.GetAttributes().Any(a =>
        {
            var fullName = a.AttributeClass?.ToDisplayString();
            var shortName = a.AttributeClass?.Name;
            if (fullName is not null &&
                (fullName.Contains("FromRoute") || fullName.Contains("FromQuery") || fullName.Contains("FromHeader")
                 || fullName.Contains("FromForm") || fullName.Contains("FromClaim") || fullName.Contains("FromCookie")))
                return true;
            return shortName is "FromRouteAttribute" or "FromRoute" or
                "FromQueryAttribute" or "FromQuery" or
                "FromHeaderAttribute" or "FromHeader" or
                "FromFormAttribute" or "FromForm" or
                "FromClaimAttribute" or "FromClaim" or
                "FromCookieAttribute" or "FromCookie";
        });
    }
}
