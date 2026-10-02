using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.Testing.SourceGenerator.Models;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     Builds a <see cref="StateTransitionModel"/> from an endpoint carrying <c>[TransitionsTo&lt;TState&gt;]</c>
///     (#7). The target state comes from the attribute (the body's <c>TransitionTo</c> is invisible across
///     metadata); the legal source states and the initial state come from the enum's <c>[InitialState]</c> /
///     <c>[TransitionFrom]</c> declarations. The create endpoint is correlated by the shared repository entity,
///     so the test can produce the entity in its initial state before POSTing the transition.
/// </summary>
internal static class StateTransitionExtractor
{
    private const string TransitionsToAttributeName = "TransitionsToAttribute";
    private const string InitialStateAttributeName = "InitialStateAttribute";
    private const string TransitionFromAttributeName = "TransitionFromAttribute";

    public static StateTransitionModel? Extract(
        INamedTypeSymbol endpointType,
        EndpointContractModel endpoint,
        IReadOnlyList<CrudCreateModel> creates)
    {
        var attr = endpointType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name == TransitionsToAttributeName);
        if (attr?.AttributeClass is not { TypeArguments.Length: 1 } attrClass)
            return null;
        if (attrClass.TypeArguments[0] is not INamedTypeSymbol { TypeKind: TypeKind.Enum } stateEnum)
            return null;
        if (attr.ConstructorArguments.Length < 1)
            return null;

        var targetState = MemberName(stateEnum, attr.ConstructorArguments[0].Value);
        if (targetState is null)
            return null;

        var entity = CrudCreateExtractor.RepositoryEntity(endpointType)
                     ?? EntityOwningState(endpointType.ContainingAssembly, stateEnum);
        if (entity is null)
            return null;

        // Correlate the create endpoint for the same entity — needed to produce it in its initial state.
        // ⚠️ A body the shape cannot fill is not a reason to refuse: the generated test hands the body to
        // PragmaticContractHost.Body, and an application that knows the foreign key supplies it there.
        // Requiring CanSynthesizeBody here is what made every transition contract in the repository a
        // skipped placeholder — the mechanism was never exercised end to end.
        var create = creates.FirstOrDefault(c => c.EntityTypeName == entity.Name);

        // The transition's own body, read exactly as the create's is. An endpoint that takes
        // nothing but its route yields no fields and keeps posting no content.
        var (transitionFields, canSynthesizeTransitionBody) = RequestBodyReader.Read(endpointType, entity, endpoint.Route);

        var initialState = InitialState(stateEnum);
        var legalSources = LegalSources(stateEnum, targetState);

        // No create route at all: emit the transition as a skipped placeholder rather than nothing. There is
        // nothing for an application to supply a body to. Returning null here made a declared [TransitionsTo]
        // vanish without a trace — the state machine looked covered while no contract existed for it.
        var reason = create is null ? UncorrelatableReason(entity.Name) : null;

        return new StateTransitionModel
        {
            Boundary = endpoint.Boundary,
            EntityName = entity.Name,
            TransitionRoute = endpoint.Route,
            CreateRoute = create?.Route ?? string.Empty,
            // The create's own operation name, carried rather than re-derived: one endpoint, one key.
            CreateOperation = create?.OperationName ?? string.Empty,
            Permission = endpoint.Permission,
            // The create's own, never the transition's: the two endpoints are gated separately.
            CreatePermission = create?.Permission,
            TargetState = targetState,
            InitialState = initialState,
            InitialToTargetIsLegal = legalSources.Contains(initialState),
            LegalSources = new EquatableArray<string>(
                System.Collections.Immutable.ImmutableArray.CreateRange(legalSources.OrderBy(s => s, System.StringComparer.Ordinal))),
            CreateFields = create?.Fields ?? EquatableArray<CrudFieldModel>.Empty,
            TransitionFields = new EquatableArray<CrudFieldModel>(transitionFields),
            CanSynthesizeTransitionBody = canSynthesizeTransitionBody,
            UngeneratableReason = reason
        };
    }

    /// <summary>
    ///     Finds the entity that owns <paramref name="stateEnum"/>: the type in the endpoint's own assembly
    ///     carrying a public property of that enum type.
    ///     <para>
    ///         Needed because the primary route — the endpoint's <c>IRepository&lt;TEntity,…&gt;</c> field — is
    ///         <b>private</b>, and private fields are stripped from reference assemblies. A test project
    ///         compiles against those, so cross-assembly (the normal layout: tests in their own project,
    ///         endpoints in the modules) that lookup always came back empty and the transition silently
    ///         produced nothing. Public properties survive into a reference assembly, so this one holds.
    ///     </para>
    /// </summary>
    private static INamedTypeSymbol? EntityOwningState(IAssemblySymbol assembly, INamedTypeSymbol stateEnum)
    {
        INamedTypeSymbol? fallback = null;

        foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
        {
            if (type.TypeKind != TypeKind.Class)
                continue;
            if (!type.GetMembers().OfType<IPropertySymbol>().Any(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    SymbolEqualityComparer.Default.Equals(p.Type, stateEnum)))
                continue;

            // A DTO carries the same state property as the entity it projects, and is usually found first.
            // Prefer the entity; keep a projection only if nothing better turns up.
            if (LooksLikeProjection(type.Name))
                fallback ??= type;
            else
                return type;
        }

        return fallback;
    }

    /// <summary>True for names that read as a projection of an entity rather than the entity itself.</summary>
    private static bool LooksLikeProjection(string name) =>
        name.EndsWith("Dto", System.StringComparison.Ordinal) ||
        name.EndsWith("Summary", System.StringComparison.Ordinal) ||
        name.EndsWith("Result", System.StringComparison.Ordinal) ||
        name.EndsWith("Request", System.StringComparison.Ordinal) ||
        name.EndsWith("Response", System.StringComparison.Ordinal) ||
        name.EndsWith("Model", System.StringComparison.Ordinal) ||
        name.EndsWith("View", System.StringComparison.Ordinal);

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
            yield return type;

        foreach (var nested in ns.GetNamespaceMembers())
            foreach (var type in EnumerateTypes(nested))
                yield return type;
    }

    /// <summary>
    ///     Explains, in the test's skip reason, why no create could be correlated.
    /// </summary>
    /// <remarks>
    ///     One cause: there is no create route for the entity. A route whose body the shape cannot fill
    ///     is <b>not</b> a cause — the application fills it through
    ///     <c>PragmaticContractHost.BodyFor</c>, and skipping instead of asking would leave this mechanism
    ///     unexercised.
    /// </remarks>
    private static string UncorrelatableReason(string entityName) =>
        $"no create endpoint was found for {entityName} — the entity is not creatable over HTTP, " +
        "so a generated test has no way to bring it into its initial state";

    /// <summary>The enum member marked <c>[InitialState]</c>, or the first member when none is marked.</summary>
    private static string InitialState(INamedTypeSymbol stateEnum)
    {
        var members = stateEnum.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue).ToList();
        var marked = members.FirstOrDefault(m =>
            m.GetAttributes().Any(a => a.AttributeClass?.Name == InitialStateAttributeName));
        return (marked ?? members.FirstOrDefault())?.Name ?? "";
    }

    /// <summary>The source states from which <paramref name="targetState"/> is reachable (its <c>[TransitionFrom]</c> set).</summary>
    private static HashSet<string> LegalSources(INamedTypeSymbol stateEnum, string targetState)
    {
        var sources = new HashSet<string>();
        var target = stateEnum.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.Name == targetState);
        if (target is null)
            return sources;

        foreach (var from in target.GetAttributes().Where(a => a.AttributeClass?.Name == TransitionFromAttributeName))
        {
            if (from.ConstructorArguments.Length < 1)
                continue;
            if (MemberName(stateEnum, from.ConstructorArguments[0].Value) is { } source)
                sources.Add(source);
        }

        return sources;
    }

    /// <summary>Resolves an enum constant value to its member name.</summary>
    private static string? MemberName(INamedTypeSymbol stateEnum, object? constantValue)
    {
        if (constantValue is null)
            return null;

        return stateEnum.GetMembers().OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, constantValue))?.Name;
    }
}
