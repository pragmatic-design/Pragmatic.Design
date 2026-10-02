using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Templates;
using Pragmatic.Testing.SourceGenerator.Transforms;

namespace Pragmatic.Testing.SourceGenerator;

/// <summary>
///     Generates contract tests for the Pragmatic application under test (#7). Phase 1: for every
///     <c>[Endpoint]</c> in the app (this assembly or any referenced module), emits the authorization contract
///     tests grouped by boundary. Later phases add CRUD, validation, tenant-isolation and transition tests.
/// </summary>
[Generator]
public sealed class ContractTestGenerator : IIncrementalGenerator
{
    private const string EndpointAttributeFullName = "Pragmatic.Endpoints.Attributes.EndpointAttribute";
    private const string EndpointsAssemblyName = "Pragmatic.Endpoints";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contracts = context.CompilationProvider.Select(static (compilation, ct) => Collect(compilation, ct));
        context.RegisterSourceOutput(contracts, static (ctx, models) => Emit(ctx, models));
    }

    /// <summary>The endpoints (auth contracts) and create endpoints (CRUD contracts) discovered in the app.</summary>
    internal sealed record CollectedContracts
    {
        public required EquatableArray<EndpointContractModel> Endpoints { get; init; }
        public required EquatableArray<CrudCreateModel> Creates { get; init; }
        public required EquatableArray<StateTransitionModel> Transitions { get; init; }

        public EquatableArray<ApiClientOperationModel> ClientOperations { get; init; } =
            EquatableArray<ApiClientOperationModel>.Empty;

        /// <summary>What was emitted per operation, and what was declined and why.</summary>
        public EquatableArray<ContractCoverageModel> Coverage { get; init; } =
            EquatableArray<ContractCoverageModel>.Empty;

        public static readonly CollectedContracts Empty = new()
        {
            Endpoints = EquatableArray<EndpointContractModel>.Empty,
            Creates = EquatableArray<CrudCreateModel>.Empty,
            Transitions = EquatableArray<StateTransitionModel>.Empty
        };
    }

    private static CollectedContracts Collect(Compilation compilation, CancellationToken ct)
    {
        // Only generate in the test project (a DLL referencing the app), and only if endpoints exist at all.
        if (compilation.GetTypeByMetadataName(EndpointAttributeFullName) is null)
            return CollectedContracts.Empty;

        // Endpoints whose body is still `throw Behavior.Pending()` are flagged by the app's source generator
        // with [assembly: PendingContract("Type")]; skip them — a not-yet-implemented endpoint has no contract,
        // and its test appears automatically once the body is implemented and the metadata is no longer emitted.
        var pending = CollectPendingContracts(compilation);

        var endpoints = ImmutableArray.CreateBuilder<EndpointContractModel>();
        var creates = ImmutableArray.CreateBuilder<CrudCreateModel>();
        var transitionCandidates = new List<(INamedTypeSymbol Type, EndpointContractModel Endpoint)>();

        // Why an operation is not a create, kept beside the operation it is about: the coverage report is
        // built from it below, and an absence with no reason is indistinguishable from a gap.
        var notACreate = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in EnumerateCandidateTypes(compilation, ct))
        {
            var endpointAttr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "EndpointAttribute");
            if (endpointAttr is null)
                continue;

            if (pending.Contains(type.ToDisplayString()))
                continue;

            var endpoint = EndpointContractExtractor.Extract(type, ResolveGroupPrefix(type));
            if (endpoint is null)
                continue;

            endpoints.Add(endpoint);

            var (create, notACreateBecause) = CrudCreateExtractor.Extract(type, endpoint);
            if (create is not null)
                creates.Add(create);
            else if (notACreateBecause is not null)
                notACreate[Key(endpoint)] = notACreateBecause;

            if (type.GetAttributes().Any(a => a.AttributeClass?.Name == "TransitionsToAttribute"))
                transitionCandidates.Add((type, endpoint));
        }

        // A create's Location is its entity's read by id: resolved once every endpoint is known, so the
        // isolation test reads with the permission that read asks for.
        var endpointList = endpoints.ToImmutable();
        var createList = creates
            .Select(create => create with { ReadPermission = ReadPermissionFor(create, endpointList) })
            .ToImmutableArray();

        // Transitions correlate to create endpoints, so resolve them once every create is known.
        var transitions = ImmutableArray.CreateBuilder<StateTransitionModel>();
        foreach (var (type, endpoint) in transitionCandidates)
        {
            var transition = StateTransitionExtractor.Extract(type, endpoint, createList);
            if (transition is not null)
                transitions.Add(transition);
        }

        var transitionList = transitions.ToImmutable();

        return new CollectedContracts
        {
            Endpoints = endpointList,
            Creates = createList,
            Transitions = transitionList,
            ClientOperations = ApiClientExtractor.Extract(compilation, ct),
            Coverage = BuildCoverage(endpointList, createList, transitionList, notACreate)
        };
    }

    /// <summary>The operation key the coverage report correlates on: one endpoint, one row.</summary>
    private static string Key(EndpointContractModel endpoint) => endpoint.Boundary + "|" + endpoint.ActionName;

    /// <summary>
    ///     What was emitted per operation, and one sentence per contract that was considered and declined.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Built from the same lists the templates are built from, and not from a second walk of the
    ///     compilation: a coverage report assembled independently is a second opinion, and the one thing
    ///     it must never be is out of step with what was emitted.
    /// </remarks>
    private static ImmutableArray<ContractCoverageModel> BuildCoverage(
        ImmutableArray<EndpointContractModel> endpoints,
        ImmutableArray<CrudCreateModel> creates,
        ImmutableArray<StateTransitionModel> transitions,
        Dictionary<string, string> notACreate)
    {
        var coverage = ImmutableArray.CreateBuilder<ContractCoverageModel>();

        foreach (var endpoint in endpoints
                     .OrderBy(e => e.Boundary, StringComparer.Ordinal)
                     .ThenBy(e => e.Route, StringComparer.Ordinal)
                     .ThenBy(e => e.ActionName, StringComparer.Ordinal))
        {
            var emitted = ImmutableArray.CreateBuilder<string>();
            var declined = ImmutableArray.CreateBuilder<string>();

            if (endpoint.RequiresPermission)
            {
                emitted.Add("auth");

                var isGet = string.Equals(endpoint.HttpMethod, "Get", StringComparison.OrdinalIgnoreCase);
                if (isGet && endpoint.Route.Contains("{") && !endpoint.AnswersWithACollection)
                    emitted.Add("not-found");
            }
            else
            {
                declined.Add(
                    "authorization: it declares no permission, so there is no caller to refuse and no "
                    + "difference to measure between one that holds it and one that does not");
            }

            var create = creates.FirstOrDefault(c => c.Boundary == endpoint.Boundary && c.OperationName == endpoint.ActionName);
            if (create is { IsDomainActionCreate: false })
            {
                emitted.Add("create");
                emitted.Add("validation");

                if (create.IsTenantScoped)
                    emitted.Add("isolation");
                else
                    declined.Add($"isolation: {create.EntityTypeName} is not an ITenantEntity, so there is no other tenant it could be invisible to");
            }
            else if (create is { IsDomainActionCreate: true })
            {
                declined.Add(
                    "create and isolation: it is a command rather than a Mutation<TEntity>, and a "
                    + "synthesised body may not meet business preconditions the generator cannot read — "
                    + "its success is not a generatable contract");
            }
            else if (string.Equals(endpoint.HttpMethod, "Post", StringComparison.OrdinalIgnoreCase)
                     && notACreate.TryGetValue(Key(endpoint), out var because))
            {
                declined.Add("create and isolation: " + because);
            }

            // Correlated by route, which is what a transition model carries: it is the endpoint's own.
            var transition = transitions.FirstOrDefault(
                t => t.Boundary == endpoint.Boundary
                     && string.Equals(t.TransitionRoute, endpoint.Route, StringComparison.Ordinal));

            if (transition is not null)
            {
                emitted.Add("transition");

                if (transition.UngeneratableReason is { Length: > 0 } reason)
                    declined.Add("transition arrangement: " + reason);
            }

            coverage.Add(new ContractCoverageModel
            {
                Boundary = endpoint.Boundary,
                Operation = endpoint.ActionName,
                HttpMethod = endpoint.HttpMethod.ToUpperInvariant(),
                Route = endpoint.Route,
                Contracts = emitted.ToImmutable(),
                NotCovered = declined.ToImmutable()
            });
        }

        return coverage.ToImmutable();
    }

    /// <summary>
    ///     The permission of the GET on <c>{create route}/{param}</c> — the read the create's Location points
    ///     at — or null when no such read is declared.
    /// </summary>
    private static string? ReadPermissionFor(CrudCreateModel create, ImmutableArray<EndpointContractModel> endpoints)
    {
        var prefix = create.Route.TrimEnd('/') + "/";

        foreach (var endpoint in endpoints)
        {
            if (!string.Equals(endpoint.HttpMethod, "Get", StringComparison.OrdinalIgnoreCase)
                || !endpoint.Route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = endpoint.Route.Substring(prefix.Length);
            if (rest.Length > 2 && rest[0] == '{' && rest[rest.Length - 1] == '}' && rest.IndexOf('/') < 0)
                return endpoint.Permission;
        }

        return null;
    }

    /// <summary>The endpoint type names flagged <c>[assembly: PendingContract]</c> in this or any referenced assembly.</summary>
    private static HashSet<string> CollectPendingContracts(Compilation compilation)
    {
        var pending = new HashSet<string>(StringComparer.Ordinal);
        AddPendingContracts(compilation.Assembly, pending);
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                AddPendingContracts(assembly, pending);
        }

        return pending;
    }

    private static void AddPendingContracts(IAssemblySymbol assembly, HashSet<string> pending)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "PendingContractAttribute"
                && attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string name)
                pending.Add(name);
        }
    }

    /// <summary>
    ///     PRAG2363 for each transition whose second half has no walk: the template emits nothing for it, and
    ///     this is how that nothing is said.
    /// </summary>
    private static void ReportUnreachableHalves(SourceProductionContext context, List<StateTransitionModel> transitions)
    {
        foreach (var transition in transitions)
        {
            var ofTheEntity = transitions.Where(t => t.EntityName == transition.EntityName).ToList();
            var walk = transition.InitialToTargetIsLegal
                ? TransitionPathFinder.ToAnIllegalSource(transition, ofTheEntity)
                : TransitionPathFinder.ToALegalSource(transition, ofTheEntity);

            if (walk is null)
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ContractDiagnostics.TransitionHalfUnreachable,
                    Location.None,
                    transition.EntityName,
                    transition.TargetState,
                    transition.InitialToTargetIsLegal ? "illegal" : "legal"));
        }
    }

    private static void Emit(SourceProductionContext context, CollectedContracts contracts)
    {
        foreach (var boundary in contracts.Endpoints.GroupBy(m => m.Boundary))
        {
            var endpoints = boundary.ToList();
            if (endpoints.Any(e => e.RequiresPermission))
                AddSource(context, new AuthContractTestTemplate(boundary.Key, endpoints).RenderOutput());
        }

        foreach (var boundary in contracts.Creates.GroupBy(m => m.Boundary))
            AddSource(context, new CrudContractTestTemplate(boundary.Key, boundary.ToList()).RenderOutput());

        foreach (var boundary in contracts.Transitions.GroupBy(m => m.Boundary))
        {
            var transitions = boundary.ToList();
            AddSource(context, new StateTransitionTestTemplate(boundary.Key, transitions).RenderOutput());
            ReportUnreachableHalves(context, transitions);
        }

        // Typed test client (two-tier: consumes the app's ApiRoutes + endpoint contracts)
        foreach (var boundary in contracts.ClientOperations.GroupBy(m => m.Boundary))
            AddSource(context, new ApiClientTemplate(boundary.Key, boundary.ToList()).RenderOutput());

        // What was emitted, and what was declined and why — the one thing the classes above cannot say.
        AddSource(context, new ContractCoverageTemplate([.. contracts.Coverage]).RenderOutput());
    }

    private static void AddSource(SourceProductionContext context, Pragmatic.SourceGen.Artifact artifact)
    {
        if (!artifact.IsEmpty)
            context.AddSource(artifact);
    }

    /// <summary>The route prefix a type inherits from the group it joined, parents included.</summary>
    /// <remarks>
    ///     ⚠️ <c>[EndpointGroup]</c> and <c>[EndpointGroup&lt;TGroup&gt;]</c> share a simple name — the
    ///     first declares a group, the second joins one — so matching on <c>Name</c> alone takes one
    ///     for the other. The arity is what separates them.
    /// </remarks>
    private static string? ResolveGroupPrefix(INamedTypeSymbol type)
    {
        var group = MembershipOf(type);
        if (group is null)
            return null;

        // Nested groups compose. The previous version read only one level, which was invisible while
        // nothing nested: a contract test would have asserted the inner prefix and missed the outer.
        var prefix = "";
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var current = group; current is not null; current = MembershipOf(current))
        {
            if (!visited.Add(current.ToDisplayString()))
                break;

            if (PrefixOf(current) is { Length: > 0 } own)
                prefix = own.TrimEnd('/') + prefix;
        }

        return prefix.Length > 0 ? prefix : null;
    }

    /// <summary>The group a type declares it belongs to, or null.</summary>
    private static INamedTypeSymbol? MembershipOf(INamedTypeSymbol type)
        => type.GetAttributes()
            .Select(a => a.AttributeClass)
            .FirstOrDefault(c => c is { Name: "EndpointGroupAttribute", TypeArguments.Length: 1 })
            ?.TypeArguments[0] as INamedTypeSymbol;

    /// <summary>The route prefix a group declares for itself.</summary>
    private static string? PrefixOf(INamedTypeSymbol group)
    {
        var declaration = group.GetAttributes().FirstOrDefault(
            a => a.AttributeClass is { Name: "EndpointGroupAttribute", TypeArguments.Length: 0 });

        return declaration is { ConstructorArguments.Length: > 0 }
               && declaration.ConstructorArguments[0].Value is string prefix
            ? prefix
            : null;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateCandidateTypes(Compilation compilation, CancellationToken ct)
    {
        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace, ct))
            yield return type;

        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            if (IsFrameworkAssembly(assembly.Name))
                continue;
            if (!MayContainEndpoints(assembly))
                continue;
            foreach (var type in GetAllTypes(assembly.GlobalNamespace, ct))
                yield return type;
        }
    }

    /// <summary>
    ///     True when the assembly could possibly declare an <c>[Endpoint]</c>, i.e. it references the assembly
    ///     that defines the attribute.
    ///     <para>
    ///         This runs on every <c>Compilation</c> change — every keystroke in the IDE — and the walk below
    ///         visits every type in every namespace. Checking the reference list first is a name comparison
    ///         against a handful of identities, and it skips the test project's whole dependency closure
    ///         (xunit, the database driver, the assertion library, the container harness), none of which can
    ///         contain an endpoint.
    ///     </para>
    /// </summary>
    private static bool MayContainEndpoints(IAssemblySymbol assembly)
    {
        foreach (var module in assembly.Modules)
        {
            foreach (var referenced in module.ReferencedAssemblies)
            {
                if (referenced.Name.StartsWith(EndpointsAssemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return type;
        }

        foreach (var nested in ns.GetNamespaceMembers())
            foreach (var type in GetAllTypes(nested, ct))
                yield return type;
    }

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Pragmatic.Endpoints", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
}
