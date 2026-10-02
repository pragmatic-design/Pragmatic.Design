using Pragmatic.SourceGenerator.Features.Actions.Transforms;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Parameter parsing methods (route, query, header, claim, form, body, dependencies).
/// </summary>
internal static partial class EndpointTransform
{
    /// <summary>Matches route parameters like <c>{id}</c>, <c>{id:guid}</c>, <c>{id?}</c>.</summary>
    private static readonly Regex RouteParameterRegex = new(@"\{(\w+)(?::(\w+))?\??}", RegexOptions.Compiled);

    /// <param name="route">The route pattern to read <c>{param}</c> placeholders from.</param>
    /// <param name="symbol">The endpoint type whose public properties the placeholders bind to.</param>
    /// <param name="impliedIdType">
    ///     The type of an <c>Id</c> this compilation will generate but the symbol does not yet have —
    ///     a load-mode mutation that declares none. Null when there is no such property.
    /// </param>
    /// <remarks>
    ///     The implied id has to be passed in rather than read: it is written by this same generator
    ///     run, so it does not exist on the symbol being analysed. Without it the route parameter
    ///     matched nothing, PRAG0504 fired on correct code, and the generated handler built the
    ///     mutation without setting a required member.
    /// </remarks>
    private static (ImmutableArray<RouteParameterModel> Matched, ImmutableArray<UnmatchedRouteParameterModel> Unmatched)
        ParseRouteParameters(string route, INamedTypeSymbol symbol, ITypeSymbol? impliedIdType = null)
    {
        // Extract {param} patterns from route
        var parameters = new List<RouteParameterModel>();
        var unmatched = new List<UnmatchedRouteParameterModel>();
        // A [FromCurrentUser] or [FromClock] property is filled by the invoker: a placeholder naming it
        // matches nothing, and PRAG0504 says so, rather than letting the URL choose whose data is read, or
        // which day is today.
        var properties = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public && !InvokerBinding.IsBound(p))
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (Match match in RouteParameterRegex.Matches(route))
        {
            var paramName = match.Groups[1].Value;
            var constraint = match.Groups[2].Success ? match.Groups[2].Value : null;
            var isOptional = match.Value.EndsWith("?}");

            if (properties.TryGetValue(paramName, out var prop))
                parameters.Add(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = prop.Name,
                    TypeName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsOptional = isOptional,
                    Constraint = constraint,
                    BindKind = BindKindClassifier.Classify(prop.Type)
                });
            else if (impliedIdType is not null
                     && string.Equals(paramName, "id", StringComparison.OrdinalIgnoreCase))
                parameters.Add(new RouteParameterModel
                {
                    Name = paramName,
                    PropertyName = "Id",
                    TypeName = impliedIdType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsOptional = isOptional,
                    Constraint = constraint,
                    BindKind = BindKindClassifier.Classify(impliedIdType)
                });
            else
                unmatched.Add(new UnmatchedRouteParameterModel
                {
                    Name = paramName,
                    ExpectedPropertyName = ToPascalCase(paramName)
                });
        }

        return (parameters.ToImmutableArray(), unmatched.ToImmutableArray());
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static ImmutableArray<HeaderParameterModel> ParseHeaderParameters(INamedTypeSymbol symbol)
    {
        var headerParams = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .Select(p =>
            {
                var headerAttr = p.GetAttributes()
                    .FirstOrDefault(a => IsFromHeaderAttribute(a));

                if (headerAttr is null)
                    return null;

                // Get header name from attribute or property name
                var headerName = p.Name;
                if (headerAttr.ConstructorArguments.Length > 0)
                    headerName = headerAttr.ConstructorArguments[0].Value?.ToString() ?? p.Name;

                // Try semantic NamedArguments first
                foreach (var namedArg in headerAttr.NamedArguments)
                    if (namedArg.Key == "Name")
                        headerName = namedArg.Value.Value?.ToString() ?? p.Name;

                // Fallback: parse from syntax when semantic info unavailable
                if (headerName == p.Name && headerAttr.ApplicationSyntaxReference is not null)
                {
                    var syntaxName = GetNamedArgumentFromSyntax(headerAttr, "Name");
                    if (!string.IsNullOrEmpty(syntaxName))
                        headerName = syntaxName!;
                }

                return new HeaderParameterModel
                {
                    HeaderName = headerName,
                    PropertyName = p.Name,
                    TypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsRequired = p.IsRequired,
                    IsRequiredByValidation = CarriesValidationRequired(p),
                    DefaultValue = GetDefaultValueLiteral(p),
                    IsInitOnly = p.SetMethod?.IsInitOnly == true,
                    InitOnlyFallback = GetInitOnlyFallback(p),
                    BindKind = BindKindClassifier.Classify(p.Type)
                };
            })
            .Where(h => h is not null)
            .Select(h => h!)
            .ToImmutableArray();

        return headerParams;
    }

    /// <summary>
    ///     Paging property names that are handled separately by the template.
    /// </summary>
    private static readonly HashSet<string> PagingPropertyNames = new(StringComparer.OrdinalIgnoreCase)
        { "Page", "PageSize" };

    /// <summary>
    ///     The wire name from <c>[JsonPropertyName("…")]</c>, or null when the author did not rename it.
    /// </summary>
    private static ImmutableArray<QueryParameterModel> ParseQueryParameters(
        INamedTypeSymbol symbol,
        ImmutableArray<RouteParameterModel> routeParams,
        ImmutableArray<HeaderParameterModel> headerParams,
        ImmutableArray<ClaimParameterModel> claimParams,
        ImmutableArray<CookieParameterModel> cookieParams,
        bool isQuery = false)
    {
        var routeParamNames = new HashSet<string>(
            routeParams.Select(r => r.PropertyName),
            StringComparer.OrdinalIgnoreCase);

        // A query binds every scalar property from the query string unless something else claims it.
        // A claim or a cookie has a source of its own; read from the query string as well, it would be
        // initialized twice (CS1912), and the URL could supply a value the declaration says comes from elsewhere.
        var boundElsewhere = new HashSet<string>(
            headerParams.Select(h => h.PropertyName)
                .Concat(claimParams.Select(c => c.PropertyName))
                .Concat(cookieParams.Select(c => c.PropertyName)),
            StringComparer.OrdinalIgnoreCase);

        // [FromCurrentUser] is never a parameter: the invoker writes it after this handler has built the
        // query, and a value read from the query string would be a caller choosing whose rows come back.
        var queryParams = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                p.SetMethod is not null &&
                !InvokerBinding.IsBound(p) &&
                !routeParamNames.Contains(p.Name) &&
                !boundElsewhere.Contains(p.Name) &&
                !IsServiceType(p.Type) &&
                (HasFromQueryAttribute(p) || (isQuery && IsQueryFilterProperty(p))))
            .Select(p => new QueryParameterModel
            {
                Name = GetQueryParameterName(p),
                PropertyName = p.Name,
                TypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsRequired = p.IsRequired,
                IsRequiredByValidation = CarriesValidationRequired(p),
                DefaultValue = GetDefaultValueLiteral(p),
                DefaultValueExpression = GetDefaultValueExpression(p),
                IsInitOnly = p.SetMethod?.IsInitOnly == true,
                InitOnlyFallback = GetInitOnlyFallback(p),
                IsValueType = p.Type.IsValueType,
                BindKind = BindKindClassifier.Classify(p.Type)
            })
            .ToList();

        // [ComplexFilter] properties: bound as string? query params, deserialized from JSON at runtime
        if (isQuery)
        {
            var complexFilterParams = symbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    p.SetMethod is not null &&
                    !InvokerBinding.IsBound(p) &&
                    !routeParamNames.Contains(p.Name) &&
                    !boundElsewhere.Contains(p.Name) &&
                    HasComplexFilterAttribute(p))
                .Select(p => new QueryParameterModel
                {
                    Name = ToCamelCase(p.Name),
                    PropertyName = p.Name,
                    TypeName = "string?",
                    IsRequired = false,
                    DefaultValue = null,
                    IsValueType = false,
                    BindKind = BindKind.String,
                    IsComplexFilter = true,
                    ComplexFilterTypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                });

            queryParams.AddRange(complexFilterParams);
        }

        return queryParams.ToImmutableArray();
    }

    /// <summary>
    ///     For Query endpoints, determines if a property should be auto-bound as a query string parameter.
    ///     Includes filter and sort properties (simple types only), excludes paging and complex filter types.
    /// </summary>
    private static bool IsQueryFilterProperty(IPropertySymbol property)
    {
        // Exclude paging properties (handled separately by the template)
        if (PagingPropertyNames.Contains(property.Name))
            return false;

        // Exclude [ComplexFilter] — handled separately with JSON deserialization
        if (HasComplexFilterAttribute(property))
            return false;

        // Bind simple/scalar types AND collections of scalars from the query string.
        // A collection-of-scalars param (List<string>, string[], IEnumerable<int>, …) binds repeated
        // query keys natively in minimal APIs (?Type=A&Type=B), enabling server-side [Filter(In)].
        // TypeAnalysis.IsScalarType already handles nullable unwrapping.
        return TypeAnalysis.IsScalarType(property.Type) || IsScalarCollection(property.Type);
    }

    /// <summary>
    ///     Determines whether a type is a collection of scalar values (List&lt;T&gt;, T[], IEnumerable&lt;T&gt;, …
    ///     with scalar T). Such params bind repeated query-string keys natively in ASP.NET Core minimal APIs.
    ///     <c>byte[]</c> is treated as a scalar (binary), not a collection.
    /// </summary>
    private static bool IsScalarCollection(ITypeSymbol type)
    {
        ITypeSymbol? elementType = type switch
        {
            IArrayTypeSymbol array when array.ElementType.SpecialType != SpecialType.System_Byte
                => array.ElementType,
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
                when named.OriginalDefinition.ToDisplayString()
                    .StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                => named.TypeArguments[0],
            _ => null
        };

        return elementType is not null && TypeAnalysis.IsScalarType(elementType);
    }

    /// <summary>
    ///     Extracts a documentable default value from the property initializer (<c>= 20</c>,
    ///     <c>= "eur"</c>, <c>= MyEnum.Active</c>) for the manifest/OpenAPI. Syntax-only on
    ///     purpose: non-constant initializers simply yield no documented default. Runtime binding
    ///     is unaffected on a <c>set</c> property — absent optional params never overwrite the
    ///     constructed instance, so the initializer wins. An <c>init</c> property is bound in the
    ///     initializer and needs the default repeated there: see <c>GetInitOnlyFallback</c>, PRAG0536.
    /// </summary>
    /// <summary>
    ///     What the query declares for <c>Page</c> or <c>PageSize</c>, walking the base chain the same
    ///     way the paging detection does.
    /// </summary>
    private static string? PagingDefaultOf(INamedTypeSymbol symbol, string name)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                if (member is IPropertySymbol { IsStatic: false } prop
                    && prop.Type.SpecialType == SpecialType.System_Int32)
                    return GetDefaultValueExpression(prop);
            }
        }

        return null;
    }

    /// <summary>
    ///     The declared initializer, written so the generated file can compile it.
    /// </summary>
    private static string? GetDefaultValueExpression(IPropertySymbol property)
    {
        var declaration = property.DeclaringSyntaxReferences.Length > 0
            ? property.DeclaringSyntaxReferences[0].GetSyntax() as PropertyDeclarationSyntax
            : null;

        var propertyType = property.Type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        } nullable
            ? nullable.TypeArguments[0]
            : property.Type;

        return declaration?.Initializer?.Value switch
        {
            LiteralExpressionSyntax lit
                when !lit.IsKind(SyntaxKind.NullLiteralExpression) &&
                     !lit.IsKind(SyntaxKind.DefaultLiteralExpression)
                => lit.ToString(),
            PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax } unary
                when unary.IsKind(SyntaxKind.UnaryMinusExpression)
                => unary.ToString(),
            // Qualified rather than copied: the author wrote `Outcome.Pending` under a using the
            // generated file does not have.
            MemberAccessExpressionSyntax member
                when propertyType.TypeKind == TypeKind.Enum
                => propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                   + "." + member.Name.Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    ///     What an optional <c>init</c> property is given when its value is absent, written for the
    ///     generated object initializer; <c>null</c> when the declared initializer cannot be reproduced.
    /// </summary>
    /// <remarks>
    ///     An <c>init</c> property can only be set in the initializer, so "absent leaves the declaration
    ///     in place" has to be spelled there: <c>value ?? &lt;what the declaration says&gt;</c>. No
    ///     initializer, and the spellings of the default (<c>null</c>, <c>default</c>, with or without
    ///     <c>!</c>), all mean what construction already produced — <c>default!</c>, the <c>!</c> saying
    ///     what the declaration said and nothing more.
    /// </remarks>
    private static string? GetInitOnlyFallback(IPropertySymbol property)
    {
        var declaration = property.DeclaringSyntaxReferences.Length > 0
            ? property.DeclaringSyntaxReferences[0].GetSyntax() as PropertyDeclarationSyntax
            : null;

        var initializer = declaration?.Initializer?.Value;
        if (initializer is PostfixUnaryExpressionSyntax suppressed
            && suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            initializer = suppressed.Operand;

        return initializer switch
        {
            null => "default!",
            LiteralExpressionSyntax lit
                when lit.IsKind(SyntaxKind.NullLiteralExpression) || lit.IsKind(SyntaxKind.DefaultLiteralExpression)
                => "default!",
            _ => GetDefaultValueExpression(property),
        };
    }

    private static string? GetDefaultValueLiteral(IPropertySymbol property)
    {
        var declaration = property.DeclaringSyntaxReferences.Length > 0
            ? property.DeclaringSyntaxReferences[0].GetSyntax() as PropertyDeclarationSyntax
            : null;

        var propertyType = property.Type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        } nullable
            ? nullable.TypeArguments[0]
            : property.Type;

        return declaration?.Initializer?.Value switch
        {
            LiteralExpressionSyntax lit
                when !lit.IsKind(SyntaxKind.NullLiteralExpression) &&
                     !lit.IsKind(SyntaxKind.DefaultLiteralExpression)
                => lit.Token.ValueText,
            PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax negated } unary
                when unary.IsKind(SyntaxKind.UnaryMinusExpression)
                => "-" + negated.Token.ValueText,
            // Enum member (MyEnum.Active) → member name; other member accesses are not constants we document.
            MemberAccessExpressionSyntax member
                when propertyType.TypeKind == TypeKind.Enum
                => member.Name.Identifier.Text,
            _ => null
        };
    }

    private const string ComplexFilterAttributeName =
        "Pragmatic.Persistence.Query.Attributes.ComplexFilterAttribute";

    private static bool HasComplexFilterAttribute(IPropertySymbol property)
        => property.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == ComplexFilterAttributeName);

    /// <summary>
    ///     Finds the private/protected fields to inject, and separately the fields whose concrete type
    ///     the generator cannot classify — those are not injected, and saying so (PRAG0527) beats
    ///     leaving the developer with a null field at the first request.
    /// </summary>
    /// <remarks>
    ///     A field typed as one of the two services a boundary registers keyed by its own type is
    ///     marked with that boundary, so the generated binding asks for it with the key instead of
    ///     without — unkeyed, nothing answers, and the endpoint had to reach for
    ///     <c>IServiceProvider</c> and write <c>typeof(TBoundary)</c> by hand.
    ///     ⚠️ The boundary is the assembly's single one. Where an assembly declares several, or none,
    ///     nothing here can say which a hand-written endpoint belongs to, and the parameter stays
    ///     unkeyed: a wrong key fails at the first request, later and quieter than what it replaces.
    /// </remarks>
    private static (ImmutableArray<DependencyModel> Dependencies, ImmutableArray<AmbiguousDependencyInfo> Ambiguous)
        ParseDependencies(INamedTypeSymbol symbol, Compilation compilation, CancellationToken ct)
    {
        // The same reading the entity side uses: an explicit [Boundary], else the one a [Module]
        // implies — and none at all in a host, which declares no boundary of its own.
        // Both spellings already carry global::, the declared one through FullyQualifiedFormat and the
        // derived one because DefaultBoundaryTransform writes it.
        var boundaries = Core.BoundaryOwnershipReader.Read(compilation, ct).Boundaries;
        var boundaryTypeName = boundaries.Length == 1 ? boundaries[0].FullTypeName : null;

        var dependencies = ImmutableArray.CreateBuilder<DependencyModel>();
        var ambiguous = ImmutableArray.CreateBuilder<AmbiguousDependencyInfo>();

        foreach (var field in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            // Exclude compiler-generated backing fields (e.g. <Prop>k__BackingField).
            if (field.IsImplicitlyDeclared)
                continue;
            if (field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Protected))
                continue;

            // The assembly overload, for the same reason the Actions side needs it: a field typed as a
            // boundary interface of this module is an error type while it is classified, because this
            // generator has not emitted the interface yet.
            switch (Core.ServiceTypeDetector.Classify(field.Type, symbol.ContainingAssembly))
            {
                case Core.ServiceTypeClassification.Service:
                    // Qualified from the same catalogue that recognised it: an error symbol cannot
                    // qualify itself, and the bare name binds only by nesting.
                    var typeName =
                        Core.ServiceTypeDetector.QualifiedGeneratedService(field.Type, symbol.ContainingAssembly)
                        ?? field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    dependencies.Add(new DependencyModel
                    {
                        FieldName = field.Name,
                        TypeName = typeName,
                        IsReadOnly = field.IsReadOnly,
                        KeyedServiceType = Actions.Transforms.BoundaryKeyedServices.IsKeyedByBoundary(typeName)
                            ? boundaryTypeName
                            : null
                    });
                    break;

                // State held in a const or static field was never a candidate for injection.
                case Core.ServiceTypeClassification.Ambiguous when !field.IsStatic && !field.IsConst:
                    ambiguous.Add(new AmbiguousDependencyInfo
                    {
                        FieldName = field.Name,
                        TypeName = field.Type.ToDisplayString()
                    });
                    break;
            }
        }

        return (dependencies.ToImmutable(), ambiguous.ToImmutable());
    }

    private static ImmutableArray<BodyPropertyModel> ParseBodyProperties(
        INamedTypeSymbol symbol,
        ImmutableArray<RouteParameterModel> routeParams,
        ImmutableArray<QueryParameterModel> queryParams,
        ImmutableArray<HeaderParameterModel> headerParams,
        ImmutableArray<ClaimParameterModel> claimParams,
        ImmutableArray<CookieParameterModel> cookieParams,
        ImmutableArray<FormParameterModel> formParams,
        Compilation compilation)
    {
        // Body properties are public properties that are NOT:
        // - Route parameters
        // - Query parameters (with [FromQuery])
        // - Header parameters (with [FromHeader])
        // - Claim parameters (with [FromClaim])
        // - Cookie parameters (with [FromCookie])
        // - Form parameters (with [FromForm])
        // - Service types

        var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in routeParams)
            excludedNames.Add(p.PropertyName);
        foreach (var p in queryParams)
            excludedNames.Add(p.PropertyName);
        foreach (var p in headerParams)
            excludedNames.Add(p.PropertyName);
        foreach (var p in claimParams)
            excludedNames.Add(p.PropertyName);
        foreach (var p in cookieParams)
            excludedNames.Add(p.PropertyName);
        foreach (var p in formParams)
            excludedNames.Add(p.PropertyName);

        var bodyProps = symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                p.SetMethod is not null &&
                !InvokerBinding.IsBound(p) &&
                !excludedNames.Contains(p.Name) &&
                !IsServiceType(p.Type) &&
                !HasNonBodyBindingAttribute(p))
            .Select(p => new BodyPropertyModel
            {
                Name = p.Name,
                TypeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsRequired = p.IsRequired,
                IsNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated,
                DefaultValueSyntax = InputPropertyHelpers.ExtractDefaultValueSyntax(p, compilation),
                Summary = GetXmlDocSummary(p),
                IsImplicit = !HasAnyBindingAttribute(p),
                IsScalar = TypeAnalysis.IsScalarType(p.Type),
                JsonName = Core.WireNameReader.Read(p),
                Constraints = Core.WireConstraintsReader.Read(p),
                IsEnum = UnwrapNullable(p.Type).TypeKind == TypeKind.Enum,
                SinceVersion = GetSinceVersion(p),
                TemporalBehavior = ReadTemporalBehavior(p)
            })
            .ToImmutableArray();

        return bodyProps;
    }

    /// <summary>
    ///     The timezone behaviour this property declares, when it declares one on a type that can carry
    ///     an instant.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Read from <see cref="AttributeNames.TemporalBehaviors" /> — one list, shared with the
    ///     Temporal feature's own pipelines — so a seventh attribute is one line in one place. A copy
    ///     here is how the two would drift, and the drift is silent: an unlisted attribute compiles,
    ///     documents itself on the generated record, and converts nothing.
    /// </remarks>
    private static string? ReadTemporalBehavior(IPropertySymbol property)
    {
        var type = UnwrapNullable(property.Type);
        var carriesAnInstant = type.SpecialType == SpecialType.System_DateTime
            || (type.Name == "DateTimeOffset"
                && type.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true });

        if (!carriesAnInstant)
            return null;

        foreach (var (attributeName, behavior) in AttributeNames.TemporalBehaviors)
            if (property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeName))
                return behavior;

        return null;
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable", TypeArguments.Length: 1 } n
            ? n.TypeArguments[0]
            : type;
}
