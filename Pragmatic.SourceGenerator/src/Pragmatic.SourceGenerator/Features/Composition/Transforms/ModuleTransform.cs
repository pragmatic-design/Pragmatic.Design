// Pragmatic.SourceGenerator - Composition - Module Transform

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Transforms [Module] attribute syntax into ModuleModel.
/// </summary>
internal static class ModuleTransform
{
    // Current attribute names
    private const string IncludeModuleGenericAttributeName = "Pragmatic.Composition.Attributes.IncludeModuleAttribute`1";

    // Deprecated attribute names (still supported)

    private const string ModuleAttributeName = "Pragmatic.Composition.Attributes.ModuleAttribute";

    // [Include<T>] attribute base name (all 3 arities share this prefix)
    private const string IncludeAttributePrefix = "Pragmatic.Composition.Attributes.IncludeAttribute";

    // [NeedsStep<T>] attribute (generic, arity-1)
    private const string NeedsStepGenericAttributeName = "Pragmatic.Composition.Attributes.NeedsStepAttribute`1";

    // [UsePackage<T>] attribute (generic, arity-1)
    private const string UsePackageAttributePrefix = "Pragmatic.Composition.Attributes.UsePackageAttribute";

    // [ExposeEndpoint<T>] and [ExposeEndpoint<T, TGroup>] attribute prefixes
    private const string ExposeEndpointAttributePrefix = "Pragmatic.Endpoints.Attributes.ExposeEndpointAttribute";

    // [RemoteBoundary<T>] attribute (generic, arity-1)
    private const string RemoteBoundaryAttributePrefix = "Pragmatic.Composition.Attributes.RemoteBoundaryAttribute";

    // [AnonymousHost] attribute full name
    private const string AnonymousHostAttributeName = "Pragmatic.Composition.Attributes.AnonymousHostAttribute";

    // [PragmaticDatabase] attribute full name
    private const string PragmaticDatabaseAttributeName =
        "Pragmatic.Composition.Attributes.PragmaticDatabaseAttribute";

    /// <summary>
    ///     Transforms a class with [Module] attribute into ModuleModel.
    /// </summary>
    public static ModuleModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var attribute = context.Attributes.FirstOrDefault();
        if (attribute is null)
            return null;

        // Get attribute properties from [Module]
        string? name = null;
        string? version = null;
        string? description = null;

        foreach (var namedArg in attribute.NamedArguments)
            switch (namedArg.Key)
            {
                case "Name":
                    name = namedArg.Value.Value as string;
                    break;
                case "Version":
                    version = namedArg.Value.Value as string;
                    break;
                case "Description":
                    description = namedArg.Value.Value as string;
                    break;
            }

        // A module names its dependencies by type, with [IncludeModule<TModule>] — no string-typed
        // spelling, which the compiler could not check.
        var allDependencies = CollectTypeDependencies(typeSymbol).Distinct().ToImmutableArray();

        // Collect [Include<T>] host wiring declarations
        var hostIncludes = CollectHostIncludes(typeSymbol);

        // Collect [NeedsStep<T>] declarations
        var needsSteps = CollectNeedsSteps(typeSymbol);

        // Collect [UsePackage<T>] declarations
        var usePackages = CollectUsePackages(typeSymbol);

        // Default name to assembly name if not specified
        var assemblyName = typeSymbol.ContainingAssembly?.Name;
        name ??= assemblyName ?? typeSymbol.ContainingNamespace?.ToDisplayString() ?? "Unknown";

        var ns = typeSymbol.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        var exposedEndpoints = CollectExposedEndpoints(typeSymbol, name);

        // Collect [RemoteBoundary<T>] declarations
        var remoteBoundaries = CollectRemoteBoundaries(typeSymbol);

        return new ModuleModel
        {
            Name = name,
            FullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = ns,
            DependsOn = allDependencies,
            Version = version,
            Description = description,
            LocationInfo = LocationInfo.From(context.TargetNode.GetLocation()),
            AssemblyName = assemblyName,
            HostIncludes = hostIncludes,
            NeedsSteps = needsSteps,
            UsePackages = usePackages,
            ExposedEndpoints = exposedEndpoints,
            RemoteBoundaries = remoteBoundaries,
            IsAnonymousHost = typeSymbol.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AnonymousHostAttributeName)
        };
    }

    /// <summary>
    ///     Collects module names from [IncludeModule&lt;T&gt;].
    /// </summary>
    /// <remarks>
    ///     There is no non-generic form: Abstractions declares only <c>IncludeModuleAttribute&lt;TModule&gt;</c>,
    ///     and a branch that looked for the other one could never run.
    /// </remarks>
    private static IEnumerable<string> CollectTypeDependencies(INamedTypeSymbol typeSymbol)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is not { IsGenericType: true })
                continue;

            var constructedFrom = attrClass.ConstructedFrom;
            var metadataName = constructedFrom.ContainingNamespace?.ToDisplayString() + "." +
                               constructedFrom.MetadataName;

            if (metadataName != IncludeModuleGenericAttributeName)
                continue;

            if (attrClass.TypeArguments.FirstOrDefault() is INamedTypeSymbol moduleType
                && GetModuleNameFromType(moduleType) is { } moduleName)
                yield return moduleName;
        }
    }

    /// <summary>
    ///     Collects fully qualified step type names from [NeedsStep&lt;T&gt;] attributes on a module class.
    /// </summary>
    private static ImmutableArray<string> CollectNeedsSteps(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            var constructedFrom = attrClass.ConstructedFrom;
            var metadataName = constructedFrom.ContainingNamespace?.ToDisplayString() + "." +
                               constructedFrom.MetadataName;

            if (metadataName != NeedsStepGenericAttributeName)
                continue;

            var typeArg = attrClass.TypeArguments.FirstOrDefault();
            if (typeArg is INamedTypeSymbol stepType)
                builder.Add(stepType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Collects [Include&lt;T&gt;] host-level wiring declarations from the module class.
    ///     All three arities are supported:
    ///     [Include&lt;TModule&gt;], [Include&lt;TModule, TDatabase&gt;], [Include&lt;TModule, TDatabase, TDbContext&gt;].
    /// </summary>
    private static ImmutableArray<HostIncludeModel> CollectHostIncludes(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<HostIncludeModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(IncludeAttributePrefix))
                continue;

            var args = attrClass.TypeArguments;
            if (args.Length == 0)
                continue;

            var moduleTypeName = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // What the include is matched on downstream: the assembly that declares the module, not
            // its name. Null when the symbol did not resolve, and the consumers fall back
            // to the name then.
            var moduleAssemblyName = args[0].ContainingAssembly?.Name;

            // 1-arity: [Include<TModule>] — no database
            if (args.Length == 1)
            {
                builder.Add(new HostIncludeModel
                {
                    ModuleTypeName = moduleTypeName,
                    ModuleAssemblyName = moduleAssemblyName,
                    LocationInfo = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation())
                });
                continue;
            }

            // 2- or 3-arity: read database info from TDatabase
            var databaseType = args[1] as INamedTypeSymbol;
            string? databaseTypeName = null;
            string? provider = null;
            string? configKey = null;
            string? migrationConfigKey = null;

            if (databaseType is not null)
            {
                databaseTypeName = databaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                (provider, configKey, migrationConfigKey) = ReadPragmaticDatabaseAttribute(databaseType);
            }

            if (args.Length == 2)
            {
                builder.Add(new HostIncludeModel
                {
                    ModuleTypeName = moduleTypeName,
                    ModuleAssemblyName = moduleAssemblyName,
                    DatabaseTypeName = databaseTypeName,
                    DatabaseProvider = provider,
                    DatabaseConfigKey = configKey,
                    MigrationConfigKey = migrationConfigKey,
                    LocationInfo = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation())
                });
                continue;
            }

            // 3-arity: also read DbContext type
            var dbContextType = args[2] as INamedTypeSymbol;
            string? dbContextTypeName = null;
            string? dbContextClassName = null;

            // ⚠️ An error symbol is skipped, not qualified. The DbContext of a boundary is
            // generated in the HOST's own compilation, so a 3-arity include that names one is reading
            // a type that does not exist yet: Roslyn hands back an IErrorTypeSymbol — which is an
            // INamedTypeSymbol — and FullyQualifiedFormat on it degrades to the name as written, with
            // no `global::` and no namespace. The host then emitted `AddDbContext<LedgerDbContext>`
            // into a file that imports nothing of the sort: CS0246 inside generated code. Treated as
            // absent, the include falls back to the 2-arity path, which now registers every boundary
            // of the module and is the right answer for exactly this case.
            if (dbContextType is not null and not IErrorTypeSymbol)
            {
                dbContextTypeName = dbContextType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                dbContextClassName = dbContextType.Name;
            }

            builder.Add(new HostIncludeModel
            {
                ModuleTypeName = moduleTypeName,
                ModuleAssemblyName = moduleAssemblyName,
                DatabaseTypeName = databaseTypeName,
                DatabaseProvider = provider,
                DatabaseConfigKey = configKey,
                // Both consumers (HostModeGenerator.Migration, PragmaticEntryTemplate) fall back to
                // DatabaseConfigKey when this is null, so dropping it here would fail nothing: the
                // migration would silently run over the least-privilege query account.
                MigrationConfigKey = migrationConfigKey,
                DbContextTypeName = dbContextTypeName,
                DbContextClassName = dbContextClassName,
                LocationInfo = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation())
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Reads Provider and ConfigKey from [PragmaticDatabase] attribute on the database type.
    /// </summary>
    private static (string? Provider, string? ConfigKey, string? MigrationConfigKey) ReadPragmaticDatabaseAttribute(
        INamedTypeSymbol databaseType)
    {
        string? provider = null;
        string? configKey = null;
        string? migrationConfigKey = null;

        foreach (var attr in databaseType.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != PragmaticDatabaseAttributeName)
                continue;

            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Provider":
                        // DatabaseProvider enum: SqlServer=0, PostgreSql=1, SQLite=2, MySql=3, InMemory=4
                        provider = namedArg.Value.Value switch
                        {
                            0 => "SqlServer",
                            1 => "PostgreSql",
                            2 => "SQLite",
                            3 => "MySql",
                            4 => "InMemory",
                            _ => namedArg.Value.Value?.ToString()
                        };
                        break;
                    case "ConfigKey":
                        configKey = namedArg.Value.Value as string;
                        break;
                    case "MigrationConfigKey":
                        migrationConfigKey = namedArg.Value.Value as string;
                        break;
                }
            }

            break;
        }

        return (provider, configKey, migrationConfigKey);
    }

    /// <summary>
    ///     Collects [UsePackage&lt;T&gt;] declarations from a module class.
    ///     Package metadata will be fused into the module's metadata in HostModeGenerator.
    /// </summary>
    private static ImmutableArray<UsePackageModel> CollectUsePackages(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<UsePackageModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(UsePackageAttributePrefix))
                continue;

            var typeArg = attrClass.TypeArguments.FirstOrDefault();
            if (typeArg is INamedTypeSymbol packageType)
            {
                // Read RoutePrefix override from attribute named args
                string? routePrefixOverride = null;
                foreach (var namedArg in attr.NamedArguments)
                {
                    if (namedArg is { Key: "RoutePrefix", Value.Value: string rp })
                        routePrefixOverride = rp;
                }

                // Resolve route prefix: attribute override ?? IPackageDefinition.RoutePrefix on T
                var routePrefix = routePrefixOverride ?? ReadPackageRoutePrefix(packageType);

                builder.Add(new UsePackageModel
                {
                    PackageTypeName = packageType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    PackageAssemblyName = packageType.ContainingAssembly?.Name ?? string.Empty,
                    RoutePrefix = routePrefix,
                    LocationInfo = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation())
                });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Tries to read IPackageDefinition.RoutePrefix from the concrete package type.
    ///     Works for source types (expression-bodied or simple return statements).
    ///     Returns null for compiled types without source access.
    /// </summary>
    private static string? ReadPackageRoutePrefix(INamedTypeSymbol packageType)
    {
        // Look for a static property named RoutePrefix on the type
        foreach (var member in packageType.GetMembers("RoutePrefix"))
        {
            if (member is not IPropertySymbol { IsStatic: true } prop)
                continue;

            // Try to read from syntax (works for source types only)
            foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
            {
                var syntax = syntaxRef.GetSyntax();
                var text = syntax.ToString();

                // Match: => "value"; or { get => "value"; } patterns
                var quoteStart = text.IndexOf('"');
                if (quoteStart >= 0)
                {
                    var quoteEnd = text.IndexOf('"', quoteStart + 1);
                    if (quoteEnd > quoteStart)
                        return text.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the module name from a module type (looks for [Module] attribute or uses assembly name).
    /// </summary>
    private static string? GetModuleNameFromType(INamedTypeSymbol moduleType)
    {
        // Look for [Module] attribute on the type to get its name
        foreach (var attr in moduleType.GetAttributes())
            if (attr.AttributeClass?.ToDisplayString() == ModuleAttributeName)
                foreach (var namedArg in attr.NamedArguments)
                    if (namedArg is { Key: "Name", Value.Value: string name })
                        return name;

        // Fallback to assembly name
        return moduleType.ContainingAssembly?.Name;
    }

    /// <summary>
    ///     Collects [RemoteBoundary&lt;T&gt;] declarations from a module class.
    /// </summary>
    private static ImmutableArray<RemoteBoundaryModel> CollectRemoteBoundaries(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<RemoteBoundaryModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(RemoteBoundaryAttributePrefix))
                continue;

            var typeArg = attrClass.TypeArguments.FirstOrDefault();
            if (typeArg is not INamedTypeSymbol moduleType)
                continue;

            // Read optional BaseUrl from named args
            string? baseUrl = null;
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg is { Key: "BaseUrl", Value.Value: string url })
                    baseUrl = url;
            }

            var moduleName = GetModuleNameFromType(moduleType) ?? moduleType.Name;

            builder.Add(new RemoteBoundaryModel
            {
                ModuleTypeName = moduleType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ModuleName = moduleName,
                BaseUrl = baseUrl,
                AssemblyName = moduleType.ContainingAssembly?.Name,
                LocationInfo = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation())
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Collects [ExposeEndpoint&lt;T&gt;] and [ExposeEndpoint&lt;T, TGroup&gt;] declarations.
    /// </summary>
    private static ImmutableArray<ExposedEndpointModel> CollectExposedEndpoints(INamedTypeSymbol typeSymbol, string moduleName)
    {
        var builder = ImmutableArray.CreateBuilder<ExposedEndpointModel>();

        foreach (var attr in typeSymbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || !attrClass.IsGenericType)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (!originalDef.StartsWith(ExposeEndpointAttributePrefix))
                continue;

            // First type arg is always the action type
            var actionType = attrClass.TypeArguments[0] as INamedTypeSymbol;
            if (actionType is null) continue;

            // Second type arg (if present) is the group type
            string? groupTypeName = null;
            if (attrClass.TypeArguments.Length > 1 && attrClass.TypeArguments[1] is INamedTypeSymbol groupType)
                groupTypeName = groupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Constructor args: HttpVerb (int enum), route (string)
            var httpVerb = "Post";
            var route = string.Empty;

            if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is int verbValue)
                httpVerb = verbValue switch
                {
                    0 => "Get",
                    1 => "Post",
                    2 => "Put",
                    3 => "Patch",
                    4 => "Delete",
                    5 => "Head",
                    6 => "Options",
                    _ => "Post"
                };

            if (attr.ConstructorArguments.Length >= 2)
                route = attr.ConstructorArguments[1].Value?.ToString() ?? string.Empty;

            // Named args
            string? name = null;
            var additionalPermissions = ImmutableArray<string>.Empty;
            var allowAnonymous = false;

            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Name" when namedArg.Value.Value is string n:
                        name = n;
                        break;
                    case "AllowAnonymous" when namedArg.Value.Value is bool aa:
                        allowAnonymous = aa;
                        break;
                    case "AdditionalPermissions" when !namedArg.Value.Values.IsDefaultOrEmpty:
                        additionalPermissions = namedArg.Value.Values
                            .Where(v => v.Value is string)
                            .Select(v => (string)v.Value!)
                            .ToImmutableArray();
                        break;
                }
            }

            builder.Add(new ExposedEndpointModel
            {
                ActionTypeName = actionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ActionSimpleName = actionType.Name,
                HttpVerb = httpVerb,
                Route = route,
                Name = name,
                GroupTypeName = groupTypeName,
                AdditionalPermissions = additionalPermissions,
                AllowAnonymous = allowAnonymous,
                ActionAssemblyName = actionType.ContainingAssembly?.Name ?? string.Empty,
                HostBoundaryName = moduleName,
                Inputs = ExposedInputsOf(actionType, httpVerb)
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     The action's settable inputs, for a verb that carries no body.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Empty for POST/PUT/PATCH, which still bind the whole action from the body — the shape
    ///         every existing <c>[ExposeEndpoint]</c> uses and the one that always worked.
    ///     </para>
    ///     <para>
    ///         ⚠️ The invoker's own properties are excluded: <c>[FromClock]</c> and
    ///         <c>[FromCurrentUser]</c> are written after binding, and offering them on the query
    ///         string would let a caller choose the day or the user. Same rule the boundary's overload
    ///         applies.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<ExposedInputModel> ExposedInputsOf(INamedTypeSymbol actionType, string httpVerb)
    {
        if (!VerbCarriesNoBody(httpVerb))
            return ImmutableArray<ExposedInputModel>.Empty;

        var inputs = ImmutableArray.CreateBuilder<ExposedInputModel>();

        for (var type = actionType; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic
                    || property.DeclaredAccessibility != Accessibility.Public
                    || property.SetMethod is null
                    || property.SetMethod.DeclaredAccessibility != Accessibility.Public
                    || IsWrittenByTheInvoker(property)
                    || inputs.Any(i => string.Equals(i.Name, property.Name, StringComparison.Ordinal)))
                    continue;

                inputs.Add(new ExposedInputModel(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    property.Type.NullableAnnotation == NullableAnnotation.Annotated,
                    property.IsRequired));
            }
        }

        return inputs.ToImmutable();
    }

    /// <summary>Get, Delete, Head and Options: a request a client sends without a body.</summary>
    internal static bool VerbCarriesNoBody(string httpVerb)
        => httpVerb is "Get" or "Delete" or "Head" or "Options";

    private static bool IsWrittenByTheInvoker(IPropertySymbol property)
        => property.GetAttributes().Any(a => a.AttributeClass?.Name
            is "FromClockAttribute" or "FromCurrentUserAttribute" or "LoadEntityAttribute"
            or "LoadCurrentUserAttribute" or "LoadEntitiesAttribute" or "LoadFromAttribute");
}
