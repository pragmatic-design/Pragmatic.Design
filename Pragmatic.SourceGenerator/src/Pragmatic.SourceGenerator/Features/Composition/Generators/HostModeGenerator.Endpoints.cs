using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Exposed endpoint collection, handler generation, and action detection helpers.
/// </summary>
internal static partial class HostModeGenerator
{
    /// <summary>
    ///     Collects exposed endpoints from local modules and discovered modules.
    /// </summary>
    private static ImmutableArray<ExposedEndpointModel> CollectExposedEndpoints(
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> discoveredModules)
    {
        var builder = ImmutableArray.CreateBuilder<ExposedEndpointModel>();

        if (!localModules.IsDefaultOrEmpty)
        {
            foreach (var module in localModules)
            {
                if (!module.ExposedEndpoints.IsDefaultOrEmpty)
                    builder.AddRange(module.ExposedEndpoints);
            }
        }

        if (!discoveredModules.IsDefaultOrEmpty)
        {
            foreach (var module in discoveredModules)
            {
                if (!module.ExposedEndpoints.IsDefaultOrEmpty)
                    builder.AddRange(module.ExposedEndpoints);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Generates endpoint handler classes for [ExposeEndpoint&lt;T&gt;] declarations.
    ///     Resolves action types from compilation to determine invoker interface and return type.
    /// </summary>
    private static void GenerateExposedEndpointHandlers(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ExposedEndpointModel> exposedEndpoints,
        Dictionary<string, string?> packageRoutePrefixMap,
        ImmutableArray<string> packageAssemblyNames,
        string rootNamespace,
        bool hasIdentityAspNetCore)
    {
        var packageSet = packageAssemblyNames.IsDefaultOrEmpty
            ? new HashSet<string>()
            : new HashSet<string>(packageAssemblyNames, StringComparer.Ordinal);

        foreach (var ep in exposedEndpoints)
        {
            // Warn if action is not from a package assembly
            if (packageSet.Count > 0 && !packageSet.Contains(ep.ActionAssemblyName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CompositionDiagnostics.ExposeEndpointNotFromPackage,
                    Location.None,
                    ep.ActionSimpleName,
                    ep.ActionAssemblyName));
            }

            // Strip "global::" prefix for GetTypeByMetadataName
            var metadataName = ep.ActionTypeName.StartsWith(GlobalPrefix, StringComparison.Ordinal)
                ? ep.ActionTypeName.Substring(GlobalPrefix.Length)
                : ep.ActionTypeName;

            var actionSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (actionSymbol is null)
                continue;

            // Detect action kind by walking base types
            var actionKind = DetectActionKind(actionSymbol);
            if (actionKind == ActionKind.Unknown)
                continue;

            // Resolve the return type for DomainAction<TReturn>
            string? returnTypeFqn = null;
            string? entityTypeFqn = null;

            if (actionKind == ActionKind.DomainAction)
            {
                returnTypeFqn = FindDomainActionReturnType(actionSymbol);
                if (returnTypeFqn is null)
                    continue;
            }
            else if (actionKind == ActionKind.Mutation)
            {
                entityTypeFqn = FindMutationEntityType(actionSymbol);
                if (entityTypeFqn is null)
                    continue;
            }

            // Read [RequirePermission] attributes from the action type
            var actionPermissions = ReadRequirePermissions(actionSymbol);

            // Enrich endpoint model with action permissions
            var enrichedEp = actionPermissions.IsEmpty
                ? ep
                : ep with { ActionPermissions = actionPermissions };

            // PRAG1681: a bodyless verb binds from the query string, which is text. An input that is
            // not a scalar, an enum or an IParsable cannot come from one, and emitting the handler
            // anyway would put a type error in a file the author did not write.
            if (UnbindableInput(enrichedEp) is { } unbindable)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CompositionDiagnostics.ExposedEndpointInputCannotBeBound, null,
                    ep.ActionSimpleName, ep.HostBoundaryName ?? "the host", ep.HttpVerb.ToUpperInvariant(),
                    unbindable.Name, unbindable.TypeName));
                continue;
            }

            var template = new ExposedEndpointHandlerTemplate(
                enrichedEp, actionKind, returnTypeFqn, entityTypeFqn, rootNamespace);
            var artifact = template.RenderOutput();
            context.AddSource(artifact);
        }
    }

    /// <summary>
    ///     The first input of a bodyless exposed endpoint that a query value cannot produce, or null.
    /// </summary>
    /// <remarks>
    ///     <c>Inputs</c> is empty for a verb that carries a body, so this asks nothing of them: they
    ///     bind the whole action from JSON, where a nested object is ordinary.
    /// </remarks>
    private static Models.ExposedInputModel? UnbindableInput(Models.ExposedEndpointModel ep)
    {
        foreach (var input in ep.Inputs)
            if (Endpoints.Models.BindKindNames.FromTypeName(input.TypeName) == Endpoints.Models.BindKind.Complex)
                return input;

        return null;
    }

    /// <summary>
    ///     Reads [RequirePermission] attribute values from an action type symbol.
    /// </summary>
    private static ImmutableArray<string> ReadRequirePermissions(INamedTypeSymbol actionSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var attr in actionSymbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();
            if (attrName is not (Endpoints.EndpointAttributeNames.RequirePermission
                or Endpoints.EndpointAttributeNames.LegacyRequirePermission))
                continue;

            foreach (var arg in attr.ConstructorArguments)
            {
                if (arg.Kind == TypedConstantKind.Array)
                {
                    foreach (var val in arg.Values)
                    {
                        var s = val.Value?.ToString();
                        if (!string.IsNullOrEmpty(s))
                            builder.Add(s!);
                    }
                }
                else
                {
                    var s = arg.Value?.ToString();
                    if (!string.IsNullOrEmpty(s))
                        builder.Add(s!);
                }
            }
        }

        return builder.Count == 0 ? ImmutableArray<string>.Empty : builder.ToImmutable();
    }

    /// <summary>
    ///     Detects whether an action type is DomainAction&lt;T&gt;, VoidDomainAction, or Mutation&lt;TEntity&gt;.
    /// </summary>
    /// <summary>
    ///     Detects action kind by walking the base type hierarchy.
    ///     Uses Name + Namespace pattern to handle multi-error generic variants
    ///     (DomainAction&lt;T&gt;, DomainAction&lt;T,E1&gt;, DomainAction&lt;T,E1,E2&gt;, etc.).
    /// </summary>
    private static ActionKind DetectActionKind(INamedTypeSymbol actionSymbol)
    {
        var current = actionSymbol.BaseType;
        while (current is not null)
        {
            var name = current.OriginalDefinition.Name;
            var ns = current.OriginalDefinition.ContainingNamespace?.ToDisplayString();

            if (name == "DomainAction" && ns == "Pragmatic.Actions.Abstractions")
                return ActionKind.DomainAction;
            if (name == "VoidDomainAction" && ns == "Pragmatic.Actions.Abstractions")
                return ActionKind.VoidDomainAction;
            if (name == "Mutation" && ns == "Pragmatic.Actions.Mutation")
                return ActionKind.Mutation;

            current = current.BaseType;
        }

        return ActionKind.Unknown;
    }

    /// <summary>
    ///     Finds the TReturn type argument from DomainAction base class (first type arg).
    /// </summary>
    private static string? FindDomainActionReturnType(INamedTypeSymbol actionSymbol)
    {
        var current = actionSymbol.BaseType;
        while (current is not null)
        {
            var name = current.OriginalDefinition.Name;
            var ns = current.OriginalDefinition.ContainingNamespace?.ToDisplayString();

            if (name == "DomainAction" && ns == "Pragmatic.Actions.Abstractions" &&
                current.TypeArguments.Length >= 1)
                return current.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            current = current.BaseType;
        }

        return null;
    }

    /// <summary>
    ///     Finds the TEntity type argument from Mutation&lt;TEntity&gt; base class.
    /// </summary>
    private static string? FindMutationEntityType(INamedTypeSymbol actionSymbol)
    {
        var current = actionSymbol.BaseType;
        while (current is not null)
        {
            if (current.OriginalDefinition.ToDisplayString() == "Pragmatic.Actions.Mutation<TEntity>" &&
                current.TypeArguments.Length == 1)
                return current.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            current = current.BaseType;
        }

        return null;
    }

    /// <remarks>
    ///     Internal so a test can render each error through its own descriptor and read the sentence a
    ///     user would see. Testing the descriptor and the arguments apart is what let the two drift far
    ///     enough that the message named nothing at all.
    /// </remarks>
    internal static ImmutableArray<ValidationError> ValidateSchemaVersions(
        ImmutableArray<AssemblyMetadataModel> assemblies)
    {
        var errors = ImmutableArray.CreateBuilder<ValidationError>();

        foreach (var assembly in assemblies)
            foreach (var entry in assembly.Entries)
            {
                var version = ParseVersion(entry.SchemaVersion);

                // Breaking: major > 1. Compatible: minor changes (e.g. 1.1 → 1.2, which added the
                // declared visibility rules to the persistence payload).
                const int expectedMajor = 1;
                const int expectedMinor = 2;

                if (version.Major > expectedMajor)
                    errors.Add(new ValidationError
                    {
                        DiagnosticId = "PRAG1610",
                        MessageArgs =
                            [entry.SchemaVersion, assembly.AssemblyName ?? "?", expectedMajor.ToString()],
                        AssemblyName = assembly.AssemblyName
                    });
                else if (version.Minor > expectedMinor)
                    // Newer minor version - warning
                    errors.Add(new ValidationError
                    {
                        DiagnosticId = "PRAG1611",
                        MessageArgs = [entry.SchemaVersion, assembly.AssemblyName ?? "?"],
                        AssemblyName = assembly.AssemblyName
                    });
            }

        return errors.ToImmutable();
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = version.Split('.');
        return (
            parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 1,
            parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
            parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0
        );
    }

    private static string GetRootNamespace(Compilation compilation)
    {
        // Try to get from assembly name
        return compilation.AssemblyName ?? "PragmaticHost";
    }


    /// <summary>
    ///     Says so when a referenced assembly declares a message-handler registration that the include
    ///     filter is about to drop.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>The registration is generated, correct, and called by nobody.</b> Discovery is by
    ///     module, and a contracts assembly deliberately is not one — so its
    ///     <c>AddPragmaticMessageHandlers</c>, with the message type registry and the transport
    ///     subscriptions it carries, is filtered out in silence. Measured on Casework as six failing
    ///     integration tests and no build signal at all.
    ///     <para>
    ///         Reported before the filter runs, because after it the assembly is gone. Only
    ///         <c>MessageHandlers</c>: it is the category measured, its registration is
    ///         <c>TryAdd</c>-based throughout and therefore safe to name in a message as "call it once",
    ///         and a diagnostic that fired on every dropped category would be about host composition in
    ///         general — which is the decision this deliberately leaves open.
    ///     </para>
    /// </remarks>
    private static void ReportUncalledMessagingRegistrations(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<AssemblyMetadataModel> assemblyMetadata,
        HashSet<string> includedAssemblyNames)
    {
        if (assemblyMetadata.IsDefaultOrEmpty)
            return;

        string? hostSource = null;

        foreach (var assembly in assemblyMetadata)
        {
            if (includedAssemblyNames.Contains(assembly.AssemblyName))
                continue;

            // ⚠️ A module left out of [Include<T>] was left out **on purpose** — that is what the
            // attribute is for, and the distributed Showcase hosts Billing without Booking exactly so.
            // What this diagnostic is about is an assembly that could not have been included whatever
            // the host wrote, because it is not a module: a contracts project, whose registration is
            // therefore absent for a reason nobody chose. Measured: without this line the first clean
            // build reported Showcase.Booking to Showcase.Billing.Host, which is a decision and not a
            // defect.
            if (assembly.Entries.Any(e => e.Category == MetadataCategoryIds.Module))
                continue;

            foreach (var entry in assembly.Entries)
            {
                if (entry.Category != MetadataCategoryIds.MessageHandlers
                    || string.IsNullOrEmpty(entry.RegistrationMethod))
                    continue;

                hostSource ??= WithoutWhitespace(compilation);
                if (HostAlreadyCalls(hostSource, entry.RegistrationMethod))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.UncalledMessagingRegistration,
                    Location.None,
                    assembly.AssemblyName,
                    entry.RegistrationMethod));
            }
        }
    }

    /// <summary>The host's own sources with every whitespace character removed, concatenated once.</summary>
    /// <remarks>
    ///     ⚠️ Whitespace goes because a call site wraps: <c>Contracts.Generated.Registration</c> on one
    ///     line and <c>.AddPragmaticMessageHandlers(app.Services)</c> on the next is the same call and
    ///     would not match a contiguous needle. Measured — the first version of this check let exactly
    ///     that through.
    /// </remarks>
    private static string WithoutWhitespace(Compilation compilation)
    {
        var text = new System.Text.StringBuilder();

        foreach (var tree in compilation.SyntaxTrees)
            foreach (var c in tree.GetText().ToString())
                if (!char.IsWhiteSpace(c))
                    text.Append(c);

        return text.ToString();
    }

    /// <summary>
    ///     Whether the host's own source already names that registration.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without this the warning fires on the application that has <b>done</b> what it asks —
    ///         the line in <c>Program.cs</c> is exactly the fix it recommends — and a diagnostic that
    ///         cannot be satisfied is one people suppress.
    ///     </para>
    ///     <para>
    ///         Matched on the <b>declaring type's full name</b>, not on the method's: every assembly's
    ///         generated registration is called <c>AddPragmaticMessageHandlers</c>, so a host that calls
    ///         one of them would silence the warning about all the others. The fallback is the type's
    ///         own name with the method, for a call written through a <c>using</c>.
    ///     </para>
    /// </remarks>
    private static bool HostAlreadyCalls(string hostSourceWithoutWhitespace, string registrationMethod)
    {
        var lastDot = registrationMethod.LastIndexOf('.');
        if (lastDot <= 0)
            return false;

        var declaringType = registrationMethod.Substring(0, lastDot);
        if (hostSourceWithoutWhitespace.IndexOf(declaringType, System.StringComparison.Ordinal) >= 0)
            return true;

        var typeDot = declaringType.LastIndexOf('.');
        if (typeDot < 0)
            return false;

        // "Class.Method", for a call site that reached the type through a using.
        var call = declaringType.Substring(typeDot + 1) + registrationMethod.Substring(lastDot);
        return hostSourceWithoutWhitespace.IndexOf(call, System.StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    ///     Builds the set of assembly names that are explicitly included via [Include&lt;T&gt;],
    ///     [UsePackage&lt;T&gt;], [RemoteBoundary&lt;T&gt;], or are the host's own assembly.
    ///     Returns null when no Include/RemoteBoundary declarations exist (backward compat: no filtering).
    /// </summary>
    private static HashSet<string>? BuildIncludedAssemblyNames(
        ImmutableArray<HostIncludeModel> hostIncludes,
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> domainModules,
        ImmutableArray<string> packageAssemblyNames,
        ImmutableArray<RemoteBoundaryModel> remoteBoundaries,
        Compilation compilation)
    {
        var result = BuildIncludedModuleAssemblies(
            hostIncludes, localModules, domainModules, remoteBoundaries, compilation);

        if (result is null)
            return null;

        // Package assemblies
        if (!packageAssemblyNames.IsDefaultOrEmpty)
            foreach (var pkg in packageAssemblyNames)
                if (!string.IsNullOrEmpty(pkg))
                    result.Add(pkg);

        return result;
    }

    /// <summary>
    ///     The assemblies of the modules this host hosts — before any package is folded in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Separate from <see cref="BuildIncludedAssemblyNames" /> because the packages cannot be
    ///     collected until this is known: a package reaches a host through a module the host hosts, and
    ///     collecting them from every module in the compilation would let a host register the invokers
    ///     of packages imported by a module it does not host.
    /// </remarks>
    private static HashSet<string>? BuildIncludedModuleAssemblies(
        ImmutableArray<HostIncludeModel> hostIncludes,
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> domainModules,
        ImmutableArray<RemoteBoundaryModel> remoteBoundaries,
        Compilation compilation)
    {
        // No filtering if there are no Include or RemoteBoundary declarations
        // (backward compat: pure monolith without explicit topology)
        if (hostIncludes.IsDefaultOrEmpty && remoteBoundaries.IsDefaultOrEmpty)
            return null;

        var result = new HashSet<string>(StringComparer.Ordinal);

        // Host's own assembly
        if (!string.IsNullOrEmpty(compilation.AssemblyName))
            result.Add(compilation.AssemblyName!);

        // Local module assemblies
        if (!localModules.IsDefaultOrEmpty)
            foreach (var m in localModules)
                if (!string.IsNullOrEmpty(m.AssemblyName))
                    result.Add(m.AssemblyName!);

        // Map Include module types to assembly names via DomainModules
        if (!hostIncludes.IsDefaultOrEmpty && !domainModules.IsDefaultOrEmpty)
        {
            var domainByBoundary = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in domainModules)
                if (!string.IsNullOrEmpty(d.BoundaryTypeName) && !string.IsNullOrEmpty(d.AssemblyName))
                    domainByBoundary[d.BoundaryTypeName!] = d.AssemblyName;

            // Also map by module name (for modules without boundary types)
            var domainByName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var d in domainModules)
                if (!string.IsNullOrEmpty(d.AssemblyName))
                    domainByName[d.Name] = d.AssemblyName;

            foreach (var include in hostIncludes)
            {
                // The assembly the include names, which is the fact. ⚠️ Without it a module whose
                // boundaries are not named after it would contribute no assembly here, so its
                // endpoints would be generated and never mapped — every one of its routes a 404.
                if (include.ModuleAssemblyName is { Length: > 0 } declaring)
                {
                    result.Add(declaring);
                    continue;
                }

                // Try by boundary type first (most precise)
                if (domainByBoundary.TryGetValue(include.ModuleTypeName, out var asmByBoundary))
                    result.Add(asmByBoundary);

                // Fallback: by module simple name
                var moduleSimpleName = GetModuleSimpleName(include.ModuleTypeName);
                if (domainByName.TryGetValue(moduleSimpleName, out var asmByName))
                    result.Add(asmByName);
            }
        }

        // RemoteBoundary module assemblies (their actions are needed for HTTP invoker registration)
        if (!remoteBoundaries.IsDefaultOrEmpty)
            foreach (var rb in remoteBoundaries)
                if (rb.AssemblyName is not null)
                    result.Add(rb.AssemblyName);

        return result;
    }

    private static string GetModuleSimpleName(string fullTypeName)
    {
        var clean = fullTypeName.Replace("global::", string.Empty);
        var lastDot = clean.LastIndexOf('.');
        var simple = lastDot >= 0 ? clean.Substring(lastDot + 1) : clean;
        return simple.EndsWith("Module", StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - 6)
            : simple;
    }

    /// <summary>
    ///     Enriches a HostIncludeModel with the MigrationDbContext FQN derived by convention.
    ///     Convention: the Persistence SG generates {DatabaseSimpleName}MigrationDbContext in the
    ///     root namespace derived from the module's boundary namespace (strip last segment + ".Entities").
    /// </summary>
    /// <remarks>
    ///     Only for a database with an entity behind it: the persistence generator writes no migration
    ///     context and no schema for one without, and a module with no entities yet is the first state of
    ///     every application. A null FQN is what every consumer skips a database on.
    /// </remarks>
    private static HostIncludeModel EnrichWithMigrationDbContext(
        HostIncludeModel include,
        Persistence.Models.PersistedStoresModel persistedStores)
    {
        if (!include.HasDatabase || include.DatabaseTypeName is null)
            return include;

        if (!persistedStores.HasDatabase(include.DatabaseTypeName))
            return include;

        // Database simple name (e.g., "ShowcaseAppDatabase")
        var dbFqn = include.DatabaseTypeName.Replace("global::", string.Empty);
        var lastDot = dbFqn.LastIndexOf('.');
        var dbSimpleName = lastDot >= 0 ? dbFqn.Substring(lastDot + 1) : dbFqn;

        // Module simple name from the include (e.g., "CatalogModule" → "Catalog")
        var moduleTypeName = include.ModuleTypeName.Replace("global::", string.Empty);
        var moduleLastDot = moduleTypeName.LastIndexOf('.');
        var moduleNs = moduleLastDot > 0 ? moduleTypeName.Substring(0, moduleLastDot) : string.Empty;

        // Derive root namespace the same way DbContextFeature does:
        // "Showcase.Catalog" → strip last segment → "Showcase"
        var rootNs = DeriveRootNamespace(moduleNs);

        var migrationFqn = string.IsNullOrEmpty(rootNs)
            ? $"global::{dbSimpleName}MigrationDbContext"
            : $"global::{rootNs}.{dbSimpleName}MigrationDbContext";

        return include with { MigrationDbContextFqn = migrationFqn };
    }

    /// <summary>
    ///     Same logic as DbContextFeature.DeriveRootNamespace — strips ".Entities" and last segment.
    /// </summary>
    private static string DeriveRootNamespace(string ns)
    {
        if (ns.EndsWith(".Entities", StringComparison.Ordinal))
            ns = ns.Substring(0, ns.Length - ".Entities".Length);

        var parts = ns.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts.Take(parts.Length - 1)) : ns;
    }
}
