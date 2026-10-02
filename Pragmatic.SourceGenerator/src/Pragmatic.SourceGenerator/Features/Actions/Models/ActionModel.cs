using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Immutable model representing a [DomainAction] class for code generation.
/// </summary>
internal sealed record ActionModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string Accessibility { get; init; }
    public required bool IsVoid { get; init; }
    public string? ReturnTypeName { get; init; }
    public bool IsInternal { get; init; }
    public bool IsSystem { get; init; }
    public string? BelongsToTypeName { get; init; }

    /// <summary>
    ///     Whether <see cref="BelongsToTypeName" /> names a package rather than a boundary.
    /// </summary>
    /// <remarks>
    ///     A package's actions declare their package with <c>[BelongsTo&lt;TPackage&gt;]</c>,
    ///     and a package has no unit of work — its actions persist through the store they hold. Read by
    ///     the invoker, which otherwise takes an <c>IUnitOfWork</c> keyed by the package type that
    ///     nothing registers, and the host does not start.
    /// </remarks>
    public bool BelongsToIsPackage { get; init; }

    /// <summary>
    /// Explicit sub-boundary name for grouping in boundary interface (e.g. "ReservationComments").
    /// When set, the action is placed in I{Boundary}{SubBoundary}Actions sub-interface.
    /// </summary>
    public string? SubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Name = …)]</c> says, verbatim, or null when it is not written.</summary>
    public string? DeclaredSubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Description = …)]</c> says: the group interface's summary.</summary>
    public string? SubBoundaryDescription { get; init; }
    public EquatableArray<DependencyModel> Dependencies { get; init; } = EquatableArray<DependencyModel>.Empty;

    /// <summary>
    ///     Private/protected fields whose concrete type could not be classified as a service or as state
    ///     (PRAG0419). They are NOT injected; the diagnostic makes that visible instead of silent.
    /// </summary>
    public EquatableArray<AmbiguousDependencyInfo> AmbiguousDependencies { get; init; } =
        EquatableArray<AmbiguousDependencyInfo>.Empty;

    /// <summary>True when [ResiliencePolicy] carries an empty/whitespace name (PRAG0420).</summary>
    public bool HasBlankResiliencePolicy { get; init; }
    public EquatableArray<LoadEntityModel> LoadEntities { get; init; } = EquatableArray<LoadEntityModel>.Empty;
    public EquatableArray<LoadEntityDiagnosticInfo> LoadEntityDiagnostics { get; init; } = EquatableArray<LoadEntityDiagnosticInfo>.Empty;

    /// <summary>The <c>[LoadFrom&lt;TQuery&gt;]</c> properties, filled by a declared query before the body.</summary>
    public EquatableArray<LoadFromQueryModel> LoadFromQueries { get; init; } = EquatableArray<LoadFromQueryModel>.Empty;

    public bool HasLoadFromQueries => !LoadFromQueries.IsDefaultOrEmpty;

    /// <summary>The <c>[FromClock]</c> and <c>[FromCurrentUser]</c> properties the invoker writes.</summary>
    public InvokerBindingsModel Bindings { get; init; } = InvokerBindingsModel.None;

    /// <summary>The <c>ValidateLoaded</c> rules the invoker calls after the preload.</summary>
    public LoadedValidationModel LoadedValidation { get; init; } = LoadedValidationModel.None;

    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public InvalidReason InvalidReason { get; init; } = InvalidReason.None;
    public bool IsValid => InvalidReason == InvalidReason.None;

    /// <summary>
    ///     The name of the type that did not resolve, as the author wrote it, for
    ///     <see cref="InvalidReason.UnresolvedResultType" />.
    /// </summary>
    public string? UnresolvedResultType { get; init; }
    public bool HasDependencies => !Dependencies.IsDefaultOrEmpty;
    public bool HasLoadEntities => !LoadEntities.IsDefaultOrEmpty;
    public EquatableArray<ActionPropertyModel> InputProperties { get; init; } = EquatableArray<ActionPropertyModel>.Empty;
    public bool HasInputProperties => !InputProperties.IsDefaultOrEmpty;
    public EquatableArray<VersionedExecuteModel> Versions { get; init; } = EquatableArray<VersionedExecuteModel>.Empty;
    public bool HasVersioning => Versions.Length > 1;
    public string FullQualifiedName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>Whether this action is marked with [CompositeAction] for BatchContext wrapping.</summary>
    public bool IsComposite { get; init; }

    /// <summary>
    ///     <c>[AbsorbsChildPermissions]</c>: this action answers for the permissions of what it
    ///     invokes, so those are not asked again while it runs.
    /// </summary>
    /// <remarks>
    ///     Its own <c>[RequirePermission]</c> is still enforced — the check runs before
    ///     <c>ExecuteActionAsync</c>, and this only covers what happens inside it. The case it exists
    ///     for is a call across a boundary: since the public boundary interface stopped entering an
    ///     internal call, an action invoking another module's operation needs the permission of that
    ///     operation, or this declaration instead. Same attribute and same meaning as on a composite
    ///     and on a mutation with children.
    /// </remarks>
    public bool AbsorbsChildPermissions { get; init; }

    /// <summary>
    ///     Whether the assembly declares any <c>[Boundary]</c>, which is what tells "no boundary here"
    ///     apart from "several, and none of them claims this operation".
    /// </summary>
    /// <remarks>
    ///     Read by PRAG0448 only. A module with no boundary at all is a library whose host supplies the
    ///     context, and saying its operations will fail to start would be false.
    /// </remarks>
    public bool AssemblyDeclaresABoundary { get; init; }

    /// <summary>
    ///     The concrete step-invoker type names (<c>{mutationFqn}.Invoker</c>) for a composite action's
    ///     <b>mutation</b> step properties. Empty for non-composite actions and for composites that
    ///     orchestrate manually in <c>Execute</c>.
    /// </summary>
    /// <remarks>
    ///     Mutations only, and deliberately: a mutation step is injected as that concrete nested type,
    ///     so it has to be registered. An action step is injected through
    ///     <c>IDomainActionInvoker&lt;,&gt;</c>, which its own registration already provides.
    /// </remarks>
    public EquatableArray<string> CompositeStepInvokerTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this composite declares step properties at all — mutations, actions or void actions
    ///     alike. When true the generated invoker delegates the whole unit of work, transaction
    ///     included, to the nested <c>CompositeInvoker</c>; otherwise the composite just wraps the
    ///     author's <c>Execute</c> in a BatchContext.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Deliberately <b>not</b> derived from <see cref="CompositeStepInvokerTypes" />. That list
    ///     is mutation-only, so a composite made of actions would read as "not a composite": its
    ///     <c>CompositeInvoker</c> would be generated, never registered and never called, and under
    ///     <c>[Transactional]</c> the outer invoker would declare a transaction the composite pipeline
    ///     opens as well — two on one connection, which EF Core refuses.
    /// </remarks>
    public bool HasCompositeSteps { get; init; }

    // === Validation metadata (SG-generated, eliminates BRIDGE reflection) ===

    /// <summary>Whether [NoValidation] is present on this action.</summary>
    public bool HasNoValidation { get; init; }

    /// <summary>Whether sync validation should run. Default true.</summary>
    public bool RunSync { get; init; } = true;

    /// <summary>Whether async validation should run, as <c>[Validate]</c> declares it.</summary>
    public bool RunAsync { get; init; }

    /// <summary>
    ///     Whether <c>[Validate]</c> is on the action. Without it, async validation runs when the
    ///     compilation declares a <c>[Validator]</c> for the action or for a nested type it validates.
    /// </summary>
    public bool ValidateIsDeclared { get; init; }

    /// <summary>Properties implementing ISyncValidator for nested validation.</summary>
    public EquatableArray<NestedValidatorProperty> SyncNestedProperties { get; init; } =
        EquatableArray<NestedValidatorProperty>.Empty;

    /// <summary>Non-primitive, non-string, non-value-type public properties for async nested validation.</summary>
    public EquatableArray<AsyncNestedValidatorProperty> AsyncNestedProperties { get; init; } =
        EquatableArray<AsyncNestedValidatorProperty>.Empty;

    // === Policy (SG-generated, eliminates reflection in PolicyEvaluationFilter) ===

    /// <summary>Fully-qualified name of the policy type from [RequirePolicy&lt;T&gt;].</summary>
    public string? PolicyTypeFullName { get; init; }
    public bool HasPolicy => PolicyTypeFullName is not null;

    // === Permission (SG-generated, eliminates reflection in PermissionAuthorizationFilter) ===

    /// <summary>Permission names from [RequirePermission] (RequireAll = true).</summary>
    public EquatableArray<string> RequireAllPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Permission names from [RequireAnyPermission] (RequireAll = false).</summary>
    public EquatableArray<string> RequireAnyPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Unresolved [RequirePermission] constant paths (e.g. <c>BookingPermissions.Entity.Op</c>)
    ///     the generator could not resolve semantically. Resolved later against the permission catalog.
    /// </summary>
    public EquatableArray<string> UnresolvedRequireAllPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Unresolved [RequireAnyPermission] constant paths.</summary>
    public EquatableArray<string> UnresolvedRequireAnyPaths { get; init; } = EquatableArray<string>.Empty;

    public bool HasPermissionRequirement => !RequireAllPermissions.IsDefaultOrEmpty || !RequireAnyPermissions.IsDefaultOrEmpty;

    public bool HasUnresolvedPermissionPaths => !UnresolvedRequireAllPaths.IsDefaultOrEmpty || !UnresolvedRequireAnyPaths.IsDefaultOrEmpty;

    /// <summary>
    ///     Names of permission attributes applied with no permission at all (e.g. a bare
    ///     <c>[RequirePermission()]</c>). Nothing else downstream records the fact — the type simply
    ///     never enters the requirement registry — so it is carried here to be reported (PRAG0422).
    /// </summary>
    public EquatableArray<string> EmptyPermissionAttributes { get; init; } = EquatableArray<string>.Empty;

    public bool HasEmptyPermissionAttribute => !EmptyPermissionAttributes.IsDefaultOrEmpty;

    // === Commit scope (PRAG0424) ===

    /// <summary>
    ///     Facades of other boundaries this action holds. Each one is a second commit scope: the inner
    ///     boundary saves before this one does, and nothing rolls it back if this one then fails.
    /// </summary>
    public EquatableArray<string> ForeignBoundaryFacades { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The same calls, for the steps that declare an undo — the answer PRAG0424 accepts, and the
    ///     subject of PRAG0429.
    /// </summary>
    public EquatableArray<string> CompensatedForeignSteps { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the type writes into its own boundary's store — a repository or a unit of work.</summary>
    public bool WritesOwnStore { get; init; }

    /// <summary><c>[AcceptsPartialWrites]</c>: the decision has been recorded, so PRAG0424 stays quiet.</summary>
    public bool AcceptsPartialWrites { get; init; }

    /// <summary>
    ///     The compensator declared by <c>[UndoWith&lt;T&gt;]</c>, fully qualified, or <c>null</c>.
    ///     When set, the generated invoker registers the undo with the request's compensation scope
    ///     after its commit succeeds.
    /// </summary>
    public string? CompensatorTypeName { get; init; }

    /// <summary>
    ///     The <c>[CommitStrategy]</c> declared on the type, or <c>null</c> for the default. Written as
    ///     the enum member name so the template can emit it without knowing the enum's values.
    /// </summary>
    public string? CommitMode { get; init; }

    /// <summary>
    ///     <c>[Transactional]</c>: the whole body is one transaction, and a call into another boundary
    ///     is an error rather than a warning (PRAG0426).
    /// </summary>
    public bool IsTransactional { get; init; }

    /// <summary>
    ///     <c>[Cacheable]</c>: the invoker answers from the cache. The Caching feature gives the type
    ///     <c>ICacheable</c> in the same compilation, which is what the invoker's override hands back.
    /// </summary>
    public bool IsCacheable { get; init; }

    /// <summary>The body invokes other actions or mutations, so how they commit is a decision.</summary>
    public bool ComposesInvocations { get; init; }


    /// <summary>Whether that decision was written down (PRAG0428).</summary>
    public bool DeclaresCommitStrategy { get; init; }

    /// <summary>The compensator was declared but does not compensate this action's return type (PRAG0425).</summary>
    public string? MismatchedCompensator { get; init; }

    /// <summary>More than one store commits inside one invocation, with no transaction spanning them.</summary>
    public int CommitScopeCount =>
        (WritesOwnStore ? 1 : 0) + (ForeignBoundaryFacades.IsDefaultOrEmpty ? 0 : ForeignBoundaryFacades.Count);

    // === Auto-derived permissions (opt-in, see ActionsFeature.AutoDerive) ===

    /// <summary>Overrides the auto-derived name — the string overload of <c>[ExplicitPermission]</c>.</summary>
    public ExplicitPermissionModel? ExplicitPermission { get; init; }

    /// <summary><c>[AllowAnonymous]</c>: no permission at all, not even a derived one.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary><c>[TenantAgnostic]</c>: the route belongs to no tenant, so none is required.</summary>
    /// <remarks>
    ///     A separate question from the one above. <c>[AllowAnonymous]</c> lifts authentication, and
    ///     the tenant refusal happens before the route runs, whoever is asking — which is why an
    ///     anonymous probe was answered 400 in a multi-tenant host.
    /// </remarks>
    public bool IsTenantAgnostic { get; init; }

    /// <summary>
    ///     Where <see cref="RequireAllPermissions" /> came from once the auto-derivation stage has run:
    ///     <c>"auto-derived"</c> or <c>"explicit"</c>. Null for a hand-written
    ///     <c>[RequirePermission]</c>, which is neither.
    /// </summary>
    public string? PermissionSource { get; init; }

    // === Contributions (nullable, opt-in) ===

    /// <summary>Resilience policy contribution from [ResiliencePolicy("name")].</summary>
    public ResilienceContribution? Resilience { get; init; }

    /// <summary>The transition <c>[TransitionsTo]</c> declares, which the invoker performs or checks.</summary>
    public TransitionModel? Transition { get; init; }
    public bool HasResilience => Resilience is not null;

    /// <summary>Filter override contribution from [WithoutFilter] and [FilterMode] attributes.</summary>
    public FilterOverrideModel? FilterOverrides { get; init; }
    public bool HasFilterOverrides => FilterOverrides?.HasOverrides == true;

    /// <summary>Delegation contribution from [StartsDelegation(nameof(…))].</summary>
    public DelegationScopeModel? DelegationScope { get; init; }
    public bool HasDelegationScope => DelegationScope is not null;

    /// <summary>Set when [StartsDelegation] names a property that is missing or is not a string.</summary>
    public string? BadDelegationSubject { get; init; }

    // === Auto-raise (#4): domain events declared via [Raises<TEvent>] on the operation ===

    /// <summary>
    ///     Domain events the action declares it raises via <c>[Raises&lt;TEvent&gt;]</c>. The generated invoker
    ///     constructs and dispatches each one after a successful commit — the entity has no behavior.
    /// </summary>
    public EquatableArray<RaisedEventModel> RaisedEvents { get; init; } = EquatableArray<RaisedEventModel>.Empty;
    public bool HasRaisedEvents => !RaisedEvents.IsDefaultOrEmpty;
}

/// <summary>
///     One <c>[ExplicitPermission]</c> on an action or mutation, in whichever of its two shapes was written: a
///     bound string, or a constant path this generator itself emits (resolved later against the permission
///     catalog, like <c>[RequirePermission]</c>).
/// </summary>
/// <remarks>
///     The path is carried unresolved because it cannot be settled in the transform: a generated constant has
///     no value yet. Resolution happens where the catalog is available. There is no third shape
///     naming an <c>IPermission</c> type.
/// </remarks>
internal sealed record ExplicitPermissionModel
{
    /// <summary>The bound string from <c>[ExplicitPermission("name")]</c>, when Roslyn could bind it.</summary>
    public string? Value { get; init; }

    /// <summary>The constant path as written, when it could not be bound (a generated constant).</summary>
    public string? ConstPath { get; init; }

}

/// <summary>
///     One <c>[Raises&lt;TEvent&gt;]</c> on an action: the event type plus its constructor arguments already
///     resolved to member-access expressions against the action instance (e.g. <c>action.DrugId</c>).
/// </summary>
internal sealed record RaisedEventModel
{
    public required string EventFullName { get; init; }
    public required EquatableArray<string> ConstructorArguments { get; init; }

    /// <summary>
    ///     Constructor parameters with no source, emitted as <c>default</c> and reported by PRAG0433.
    /// </summary>
    public EquatableArray<string> UnmatchedParameters { get; init; } = EquatableArray<string>.Empty;
}

internal enum InvalidReason
{
    None,
    NotPartial,
    NoBaseType,

    /// <summary>
    ///     Declared inside another type. The generated partial does not reproduce the containing-type
    ///     chain, so it declares a namespace-level class of the same simple name instead of extending
    ///     the nested one — and the invoker inside it fails to override a base generic over a type it
    ///     is not.
    /// </summary>
    Nested,

    /// <summary>
    ///     The type the action returns does not resolve — one missing <c>using</c> in the author's own
    ///     file, most often.
    /// </summary>
    /// <remarks>
    ///     Roslyn hands the generator an <b>error symbol</b>, which is still an
    ///     <c>INamedTypeSymbol</c> — so every check passes and its fully-qualified display is the bare
    ///     name with no namespace. Writing that back out turns one authoring error into a page of
    ///     <c>CS0246</c>/<c>CS0400</c> inside files the author cannot edit, and those read as a
    ///     generator bug, and get reported against templates that are already correct. Nothing is generated for the operation; the compiler reports the
    ///     real error where it is, and <c>PRAG9001</c> names the operation it stopped.
    /// </remarks>
    UnresolvedResultType
}

internal sealed record LoadEntityDiagnosticInfo
{
    public required string EntityTypeName { get; init; }
    public required LoadEntityDiagnosticKind Kind { get; init; }

    /// <summary>The attribute the diagnostic is on — <c>LoadEntity</c> or <c>LoadEntities</c> — as the message names it.</summary>
    public string AttributeName { get; init; } = "LoadEntity";
    public string? IdPropertyName { get; init; }

    /// <summary>What to declare instead, for <c>KeyTypeMismatch</c>: a key, or a list of keys.</summary>
    public string? KeyAdvice { get; init; }

    /// <summary>The key property's type, for <c>KeyTypeMismatch</c>.</summary>
    public string? SuppliedKeyType { get; init; }

    /// <summary>The entity's key type, for <c>KeyTypeMismatch</c>.</summary>
    public string? EntityKeyType { get; init; }

    /// <summary>The include path, for <c>IncludeNotANavigation</c>.</summary>
    public string? IncludePath { get; init; }

    /// <summary>The segment of <see cref="IncludePath" /> that names no navigation.</summary>
    public string? IncludeSegment { get; init; }

    /// <summary>The <c>Specification</c> the attribute names, for the specification kinds.</summary>
    public string? SpecificationName { get; init; }

    /// <summary>Why the declaration cannot be generated, for the kinds whose message carries a reason.</summary>
    public string? Reason { get; init; }

    /// <summary>The logic key member <c>By</c> names, for <c>ByKeyTypeMismatch</c>.</summary>
    public string? LogicKeyMember { get; init; }
}

internal enum LoadEntityDiagnosticKind
{
    IdPropertyNotFound,
    KeyTypeNotFound,
    KeyTypeMismatch,
    IncludeNotANavigation,

    /// <summary>The <c>Specification</c> names no static specification of the entity.</summary>
    SpecificationNotFound,

    /// <summary>A parameter of the rule binds no property of the operation.</summary>
    SpecificationParameterUnbound,

    /// <summary>Both a key and a <c>Specification</c>, or neither.</summary>
    KeyOrSpecification,

    /// <summary>An input of a <c>[LoadFrom]</c> query that binds no property of the operation.</summary>
    LoadFromInputUnbound,

    /// <summary><c>By</c> names no single-part logic key of the entity, or stands beside a <c>Specification</c>.</summary>
    ByNotTheLogicKey,

    /// <summary>The key property's type is not the logic key's.</summary>
    ByKeyTypeMismatch,

    /// <summary>A <c>[RequireExists]</c> beside a <c>[LoadEntity]</c> of the same entity and key.</summary>
    ExistsBesideALoad
}

internal sealed record ActionPropertyModel
{
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required bool IsRequired { get; init; }
    public bool IsNullable { get; init; }
    public string? DefaultValueSyntax { get; init; }
    public bool HasDefaultValue => DefaultValueSyntax is not null;

    /// <summary>
    ///     Whether the default can appear after <c>=</c> in a parameter list.
    /// </summary>
    /// <remarks>
    ///     A property initialiser is an expression; a parameter default must be a compile-time
    ///     constant. <c>= []</c> on a <c>List&lt;string&gt;</c> is legal on the request body record and
    ///     illegal in the boundary overload's signature, where it became
    ///     <c>List&lt;string&gt; aliases = []</c> and broke the build with CS1736 inside a generated
    ///     file. Two consumers, one syntax, so the distinction is carried rather than recomputed —
    ///     dropping the initialiser everywhere instead turned "starts empty" into a null the entity
    ///     refused at save time.
    /// </remarks>
    public bool DefaultIsCompileTimeConstant { get; init; }

    /// <summary>
    ///     Whether the invoker writes it — <c>[FromClock]</c>, <c>[FromCurrentUser]</c> — so no caller
    ///     passes it: a boundary overload taking it would not compile, since only the nested invoker
    ///     reaches the setter.
    /// </summary>
    public bool IsBoundByTheInvoker { get; init; }
}

internal sealed record VersionedExecuteModel
{
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required string MethodName { get; init; }
    public string VersionString => $"{Major}.{Minor}";
}

internal sealed record DependencyModel
{
    public required string FieldName { get; init; }
    public required string TypeName { get; init; }
    public bool IsReadOnly { get; init; }
    public string? KeyedServiceType { get; init; }

    /// <summary>
    ///     Whether this dependency's type is <c>internal</c> to the module that declares it.
    /// </summary>
    /// <remarks>
    ///     True for the boundary's <c>internal</c> facade and nothing else. The invoker that takes it
    ///     is emitted <c>internal</c> too — a type cannot be more accessible than what its constructor
    ///     takes, which is what <c>CS0051</c> says.
    ///     <para>
    ///     ⚠️ This works because the host calls each module's own registration, so nothing outside the
    ///     module names these types. A host that named them from another assembly would force the
    ///     invoker to stay <c>public</c> and pull the facade out of the <c>IServiceProvider</c> — a
    ///     service locator, whose failure moves from container validation to first construction.
    ///     </para>
    /// </remarks>
    public bool IsInternalType { get; init; }
}
