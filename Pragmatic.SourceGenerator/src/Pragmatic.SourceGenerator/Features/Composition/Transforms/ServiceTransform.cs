using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms [Service] attribute declarations into ServiceModel.
/// </summary>
internal static class ServiceTransform
{
    private const string InjectAttributeName = "Pragmatic.Composition.Attributes.InjectAttribute";
    private const string ProvidedByHostAttributeName = "Pragmatic.Composition.Attributes.ProvidedByHostAttribute";

    /// <summary>The names of <c>Pragmatic.Composition.Attributes.Lifetime</c>, by its values.</summary>
    private static readonly string[] LifetimeNames = ["Singleton", "Scoped", "Transient"];

    /// <summary>
    ///     What the contract says about who registers it: whether the host does
    ///     (<c>[ProvidedByHost]</c>, so this compilation cannot see the registration), and with which
    ///     lifetime when it names one.
    /// </summary>
    /// <remarks>
    ///     The original definition, so a declaration on <c>IRepository&lt;T&gt;</c> covers every
    ///     <c>IRepository&lt;Invoice&gt;</c> a service asks for — attributes are not substituted onto a
    ///     constructed type, and reading them from the constructed one found nothing.
    /// </remarks>
    private static (bool Provided, string? Lifetime) HostProvision(ITypeSymbol type)
    {
        var declaration = (type is INamedTypeSymbol named ? named.OriginalDefinition : type)
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ProvidedByHostAttributeName);

        if (declaration is null)
            return (false, null);

        if (declaration.ConstructorArguments.Length != 1 || declaration.ConstructorArguments[0].Value is not int value)
            return (true, null);

        return (true, value >= 0 && value < LifetimeNames.Length ? LifetimeNames[value] : null);
    }

    /// <summary>
    ///     Transforms a class with [Service] attribute into a ServiceModel.
    /// </summary>
    public static ServiceModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        return TransformInternal(context, false, ct);
    }

    /// <summary>
    ///     Transforms a class with [Service&lt;TInterface&gt;] attribute into a ServiceModel.
    /// </summary>
    public static ServiceModel? TransformGeneric(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        return TransformInternal(context, true, ct);
    }

    private static ServiceModel? TransformInternal(
        GeneratorAttributeSyntaxContext context,
        bool isGenericAttribute,
        CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        // Validate: must be a class
        if (symbol.TypeKind != TypeKind.Class)
            return CreateInvalidModel(symbol, InvalidReason.NotClass);

        // Validate: cannot be abstract
        if (symbol.IsAbstract)
            return CreateInvalidModel(symbol, InvalidReason.Abstract);

        var attributeData = context.Attributes.FirstOrDefault();
        if (attributeData is null)
            return null;

        // Parse attribute arguments
        var (lifetime, asSelf, key, multiple) = ParseServiceAttribute(attributeData);

        // Determine service type
        // For generic attribute [Service<T>], extract interface from type argument
        INamedTypeSymbol? explicitInterfaceFromGeneric = null;
        if (isGenericAttribute && attributeData.AttributeClass?.TypeArguments.Length > 0)
            explicitInterfaceFromGeneric = attributeData.AttributeClass.TypeArguments[0] as INamedTypeSymbol;

        var (serviceTypeName, hasNoInterface, isOpenGeneric, openGenericServiceTypeName,
                openGenericImplementationTypeName) =
            DetermineServiceType(symbol, attributeData, asSelf, explicitInterfaceFromGeneric);

        // A [Service<T>] whose type argument another generator will write is an error symbol here, and
        // FullyQualifiedFormat then hands back the name exactly as typed — unqualified, so the emitted
        // registration binds only if the generated file happens to carry a matching using.
        var unresolvedTypeArgument = UnresolvedArgumentOf(explicitInterfaceFromGeneric);

        // Get constructor dependencies
        var dependencies = GetConstructorDependencies(symbol);

        // Get property and method injections
        var propertyInjections = GetPropertyInjections(symbol);
        var methodInjections = GetMethodInjections(symbol);

        return new ServiceModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ServiceTypeName = serviceTypeName,
            Lifetime = lifetime,
            AsSelf = asSelf,
            Key = key,
            IsMultiple = multiple,
            Dependencies = dependencies,
            PropertyInjections = propertyInjections,
            MethodInjections = methodInjections,
            LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
            InvalidReason = hasNoInterface ? InvalidReason.NoInterface : InvalidReason.None,
            UnresolvedTypeArgument = unresolvedTypeArgument,
            IsOpenGeneric = isOpenGeneric,
            OpenGenericServiceTypeName = openGenericServiceTypeName,
            OpenGenericImplementationTypeName = openGenericImplementationTypeName
        };
    }

    private static (string Lifetime, bool AsSelf, string? Key, bool Multiple) ParseServiceAttribute(AttributeData attributeData)
    {
        var lifetime = "Scoped";
        var asSelf = false;
        string? key = null;
        var multiple = false;

        foreach (var namedArg in attributeData.NamedArguments)
            switch (namedArg.Key)
            {
                case "Lifetime":
                    lifetime = namedArg.Value.Value switch
                    {
                        0 => "Singleton",
                        1 => "Scoped",
                        2 => "Transient",
                        _ => "Scoped" // Unknown enum value — default to Scoped (ServiceLifetime.Scoped = 1)
                    };
                    break;
                case "AsSelf":
                    asSelf = namedArg.Value.Value is true;
                    break;
                case "Key":
                    key = namedArg.Value.Value as string;
                    break;
                case "Multiple":
                    multiple = namedArg.Value.Value is true;
                    break;
            }

        return (lifetime, asSelf, key, multiple);
    }

    /// <summary>
    ///     The first type argument that does not resolve to a real type, or <c>null</c> when they all
    ///     do.
    /// </summary>
    private static string? UnresolvedArgumentOf(INamedTypeSymbol? explicitInterface)
    {
        if (explicitInterface is null)
            return null;

        foreach (var argument in explicitInterface.TypeArguments)
            if (argument.TypeKind == TypeKind.Error)
                return argument.ToDisplayString();

        return null;
    }

    private static (string ServiceTypeName, bool HasNoInterface, bool IsOpenGeneric, string? OpenGenericServiceTypeName,
        string? OpenGenericImplementationTypeName)
        DetermineServiceType(INamedTypeSymbol symbol, AttributeData attributeData, bool asSelf,
            INamedTypeSymbol? explicitInterface = null)
    {
        var hasNoInterface = false;
        var isOpenGeneric = false;
        string? openGenericServiceTypeName = null;
        string? openGenericImplementationTypeName = null;
        string serviceTypeName;

        // 1. If explicit interface from [Service<T>], use it directly
        if (explicitInterface is not null)
        {
            serviceTypeName = explicitInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return (serviceTypeName, hasNoInterface, isOpenGeneric, openGenericServiceTypeName,
                openGenericImplementationTypeName);
        }

        // 2. Check for open generic: class Repository<T> : IRepository<T>
        if (symbol.TypeParameters.Length > 0 && !asSelf)
        {
            var matchingInterface = FindMatchingGenericInterface(symbol);
            if (matchingInterface is not null)
            {
                isOpenGeneric = true;
                openGenericServiceTypeName = BuildOpenGenericTypeof(matchingInterface);
                openGenericImplementationTypeName = BuildOpenGenericTypeof(symbol);
                serviceTypeName = matchingInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else
            {
                hasNoInterface = true;
                serviceTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }
        // 3. As = typeof(T) takes precedence over AsSelf — matches the documented ServiceAttribute
        // contract ("As takes precedence and AsSelf is ignored when both are set"). Ordered the other
        // way, AsSelf would silently win and As would be dropped.
        else if (attributeData.NamedArguments.FirstOrDefault(a => a.Key == "As").Value.Value is INamedTypeSymbol asTypeSymbol)
        {
            serviceTypeName = asTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        // 4. AsSelf registration
        else if (asSelf)
        {
            serviceTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        // 5. Fallback: use first interface (or self if none)
        else
        {
            var firstInterface = symbol.Interfaces.FirstOrDefault();
            if (firstInterface is null)
            {
                hasNoInterface = true;
                serviceTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            else
            {
                serviceTypeName = firstInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return (serviceTypeName, hasNoInterface, isOpenGeneric, openGenericServiceTypeName,
            openGenericImplementationTypeName);
    }

    /// <summary>
    ///     Finds an interface where the type arguments match the class type parameters.
    /// </summary>
    private static INamedTypeSymbol? FindMatchingGenericInterface(INamedTypeSymbol symbol)
    {
        var typeParams = symbol.TypeParameters;

        foreach (var iface in symbol.Interfaces)
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.TypeArguments.Length == typeParams.Length)
            {
                var allMatch = true;
                for (var i = 0; i < typeParams.Length; i++)
                    if (!SymbolEqualityComparer.Default.Equals(iface.TypeArguments[i], typeParams[i]))
                    {
                        allMatch = false;
                        break;
                    }

                if (allMatch)
                    return iface;
            }
        }

        return null;
    }

    /// <summary>
    ///     Builds typeof expression for open generic type.
    /// </summary>
    private static string BuildOpenGenericTypeof(INamedTypeSymbol type)
    {
        var definition = type.ConstructedFrom ?? type;
        var baseName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var openBracket = baseName.IndexOf('<');
        if (openBracket < 0)
            return $"typeof({baseName})";

        var prefix = baseName.Substring(0, openBracket);
        var numParams = definition.TypeParameters.Length;
        var commas = numParams > 1 ? new string(',', numParams - 1) : "";

        return $"typeof({prefix}<{commas}>)";
    }

    /// <summary>
    ///     The greediest public constructor's parameters, as dependencies.
    /// </summary>
    /// <remarks>
    ///     Internal because a job is resolved from the container exactly like a service and its
    ///     dependencies were checked by nobody. One reading, so the two cannot disagree about
    ///     what an optional dependency is or about who declares <c>[ProvidedByHost]</c>.
    /// </remarks>
    internal static ImmutableArray<DependencyModel> GetConstructorDependencies(INamedTypeSymbol symbol)
    {
        var constructor = symbol.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (constructor is null)
            return ImmutableArray<DependencyModel>.Empty;

        return constructor.Parameters
            .Select(DependencyOf)
            .ToImmutableArray();
    }

    /// <summary>A constructor or injected-method parameter, as a dependency of the service.</summary>
    /// <remarks>
    ///     ⚠️ A boundary interface of this module — <c>I{Boundary}Actions</c>, <c>I{Boundary}InternalActions</c>,
    ///     a group's — is emitted later in this same compilation, so here it is an error type with a bare
    ///     name, and the dependency check found no registration for a bare name: <c>PRAG1641</c> on an
    ///     interface the host registers. The catalogue the operations' fields already use
    ///     qualifies it, and only a name a boundary here emits; the host's generated wiring registers it,
    ///     scoped, so it is declared provided by the host with that lifetime — which keeps the captive
    ///     check of a singleton that takes one.
    /// </remarks>
    private static DependencyModel DependencyOf(IParameterSymbol parameter)
    {
        var generated = ServiceTypeDetector.QualifiedGeneratedService(parameter.Type, parameter.ContainingAssembly);
        var (provided, lifetime) = generated is not null ? (true, "Scoped") : HostProvision(parameter.Type);

        return new DependencyModel
        {
            TypeName = parameter.Type.Name,
            FullTypeName = generated ?? parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsOptional = parameter.IsOptional || parameter.NullableAnnotation == NullableAnnotation.Annotated,
            Key = GetKeyedServiceKey(parameter),
            IsProvidedByHost = provided,
            ProvidedByHostLifetime = lifetime
        };
    }

    private static ImmutableArray<PropertyInjectionModel> GetPropertyInjections(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == InjectAttributeName))
            .Select(CreatePropertyInjectionModel)
            .ToImmutableArray();
    }

    private static ImmutableArray<MethodInjectionModel> GetMethodInjections(INamedTypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary &&
                        m.GetAttributes().Any(a =>
                            a.AttributeClass?.ToDisplayString() == InjectAttributeName))
            .Select(CreateMethodInjectionModel)
            .ToImmutableArray();
    }

    private static PropertyInjectionModel CreatePropertyInjectionModel(IPropertySymbol property)
    {
        var injectAttr = property.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == InjectAttributeName);

        var isRequired = false;
        string? key = null;

        if (injectAttr is not null)
            foreach (var arg in injectAttr.NamedArguments)
                switch (arg.Key)
                {
                    case "Required":
                        isRequired = arg.Value.Value is true;
                        break;
                    case "Key":
                        key = arg.Value.Value as string;
                        break;
                }

        var (provided, hostLifetime) = HostProvision(property.Type);

        return new PropertyInjectionModel
        {
            PropertyName = property.Name,
            PropertyTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsRequired = isRequired,
            Key = key,
            IsProvidedByHost = provided,
            ProvidedByHostLifetime = hostLifetime
        };
    }

    private static MethodInjectionModel CreateMethodInjectionModel(IMethodSymbol method)
    {
        var parameters = method.Parameters
            .Select(DependencyOf)
            .ToImmutableArray();

        return new MethodInjectionModel
        {
            MethodName = method.Name,
            Parameters = parameters
        };
    }

    private static string? GetKeyedServiceKey(IParameterSymbol parameter)
    {
        var fromKeyedAttr = parameter.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == "FromKeyedServicesAttribute");

        if (fromKeyedAttr?.ConstructorArguments.Length > 0)
            return fromKeyedAttr.ConstructorArguments[0].Value?.ToString();

        return null;
    }

    private static ServiceModel CreateInvalidModel(INamedTypeSymbol symbol, InvalidReason reason)
    {
        return new ServiceModel
        {
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            TypeName = symbol.Name,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ServiceTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Lifetime = "Scoped",
            AsSelf = false,
            Dependencies = ImmutableArray<DependencyModel>.Empty,
            LocationInfo = LocationInfo.From(symbol.Locations.FirstOrDefault()),
            InvalidReason = reason
        };
    }
}
