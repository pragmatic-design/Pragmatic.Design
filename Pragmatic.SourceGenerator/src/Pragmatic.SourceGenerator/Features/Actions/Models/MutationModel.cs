using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Compositions.Models;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

internal sealed record MutationModel
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string Accessibility { get; init; }
    public required string EntityTypeName { get; init; }
    public required string EntityFullTypeName { get; init; }
    public string? EntityIdTypeName { get; init; }
    public required MutationModeValue Mode { get; init; }
    public MutationReturnTypeValue ReturnType { get; init; } = MutationReturnTypeValue.Entity;

    /// <summary>
    ///     The entity's <c>[LogicKey]</c> parts, in key order, when <see cref="ReturnType" /> is
    ///     <c>LogicalKey</c>; empty otherwise.
    /// </summary>
    public EquatableArray<MutationKeyPartModel> LogicalKeyParts { get; init; } = EquatableArray<MutationKeyPartModel>.Empty;

    /// <summary>
    ///     Why a <c>LogicalKey</c> mutation cannot return its key (PRAG0403), or <c>null</c>. While set,
    ///     the mutation is generated as if it returned the entity, so the build stops on the diagnostic
    ///     and not on a generated file.
    /// </summary>
    public string? LogicalKeyProblem { get; init; }

    /// <summary>What the mutation returns, once a <c>LogicalKey</c> that cannot be read is set aside.</summary>
    public MutationReturnTypeValue EffectiveReturnType
        => ReturnType == MutationReturnTypeValue.LogicalKey && LogicalKeyProblem is not null
            ? MutationReturnTypeValue.Entity
            : ReturnType;
    public EquatableArray<string> Includes { get; init; } = EquatableArray<string>.Empty;
    public string? IdPropertyName { get; init; }

    /// <summary>
    ///     Whether this generator writes the <c>Id</c> property, because the mutation loads a row and
    ///     the author declared none.
    /// </summary>
    /// <remarks>
    ///     A mode that loads is addressed by an id: that is implied by the role, not chosen. Requiring
    ///     the author to write it made every Update and Delete carry the same line, and forgetting it
    ///     produced an operation that compiled, shipped and found nothing on every call. Declaring your
    ///     own still wins — the rule is the one the traits use.
    /// </remarks>
    public bool GeneratesIdProperty { get; init; }

    /// <summary>
    ///     The DTO this mutation declares with <c>[ReturnsDto&lt;T&gt;]</c>, when that DTO maps from an
    ///     entity — so it has a generated <c>RequiredNavigations</c>. It answers only while
    ///     <see cref="EffectiveReturnType" /> is <c>Entity</c>; beside a key the endpoint reports PRAG0535.
    /// </summary>
    /// <remarks>
    ///     Carried so the invoker can load what the response needs. The navigation list is not carried:
    ///     Mapping computes it — explicit paths, auto-flattening, nested DTOs, collections — and
    ///     publishes it on the DTO. The invoker names <c>{Dto}.RequiredNavigations</c> at runtime, the
    ///     same way it names <c>{Dto}.FromEntity</c>: one rule, in the feature that owns it.
    /// </remarks>
    public string? ResponseDtoFullTypeName { get; init; }

    /// <summary>
    ///     Whether the id this mutation loads by is of the wrong type (PRAG0435). Only ever set for a
    ///     mode that loads an existing row and declares its own id — Create has nothing to address, and
    ///     a missing one is now generated rather than reported.
    /// </summary>
    public MutationIdProblem IdProblem { get; init; } = MutationIdProblem.None;
    public EquatableArray<DependencyModel> Dependencies { get; init; } = EquatableArray<DependencyModel>.Empty;

    /// <summary>
    ///     Private/protected fields whose concrete type could not be classified as a service or as state
    ///     (PRAG0419). They are NOT injected; the diagnostic makes that visible instead of silent.
    /// </summary>
    public EquatableArray<AmbiguousDependencyInfo> AmbiguousDependencies { get; init; } =
        EquatableArray<AmbiguousDependencyInfo>.Empty;

    /// <summary>True when [ResiliencePolicy] carries an empty/whitespace name (PRAG0420).</summary>
    public bool HasBlankResiliencePolicy { get; init; }

    /// <summary>
    ///     Whether the mutation declares <c>[QueryStrategy]</c> — an option a write cannot honour.
    /// </summary>
    /// <remarks>
    ///     The attribute targets a class, so it binds here as readily as on a query, and on a query it
    ///     <em>is</em> honoured. A write loads its row tracked, because it is about to change it, and the
    ///     filters it lifts are declared with <c>[FilterMode]</c> / <c>[WithoutFilter&lt;T&gt;]</c>. So
    ///     the declaration is read by nobody, and <c>PRAG0703</c> says so rather than leaving it silent.
    /// </remarks>
    public bool HasInertQueryStrategy { get; init; }
    public EquatableArray<MutationPropertyMapModel> MappedProperties { get; init; } = EquatableArray<MutationPropertyMapModel>.Empty;

    /// <summary>Mutation properties that could not be mapped to an entity setter (for diagnostics).</summary>
    public EquatableArray<UnmappedMutationPropertyModel> UnmappedProperties { get; init; } = EquatableArray<UnmappedMutationPropertyModel>.Empty;

    /// <summary>
    ///     Children of the aggregate the mutation carries — merged into the entity, not assigned to it.
    /// </summary>
    public EquatableArray<MutationChildModel> Children { get; init; } = EquatableArray<MutationChildModel>.Empty;

    /// <summary>
    ///     The lists of keys this mutation uses to choose which rows a navigation points at.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Children" /> because they are written somewhere else entirely:
    ///     a child is merged inside <c>ApplyToEntity</c>, a link is attached to the change tracker by
    ///     the invoker, through a capability the repository implements. <c>ApplyToEntity</c> has no
    ///     <c>DbContext</c> and Actions has no EF Core.
    /// </remarks>
    public EquatableArray<MutationLinkModel> Links { get; init; } = EquatableArray<MutationLinkModel>.Empty;

    /// <summary>
    ///     The aggregate this mutation's entity declares itself part of, if any.
    /// </summary>
    /// <remarks>
    ///     A mutation on a child contradicts its own <c>[PartOf&lt;TParent&gt;]</c>: one says "written
    ///     through the parent", the other says "addressed directly". PRAG0438 reports the pair.
    /// </remarks>
    public string? TargetsAChildAggregate { get; init; }

    /// <summary>
    ///     Whether that <c>[PartOf]</c> claims to be the only way in.
    /// </summary>
    /// <remarks>
    ///     Kept beside the fact rather than folded into it: «this entity is part of an aggregate» and
    ///     «and therefore may not be addressed on its own» are two statements, and only the second is
    ///     a policy. <c>PRAG0438</c> reads this one.
    /// </remarks>
    public bool ChildAggregateIsExclusive { get; init; } = true;

    /// <summary>
    ///     Mapping is referenced, so the body of the write is Mapping's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the <c>FeatureDetector</c>'s answer, not an agreement between the two generators. A
    ///     mutation is a mapping classified by a different attribute: where Mapping is present it writes
    ///     the body — with converters, renames and declared targets — and what stays here is the override
    ///     the invoker calls. Where it is absent, this template writes the body itself.
    /// </remarks>
    public bool MappingOwnsTheBody { get; init; }

    /// <summary>
    ///     The properties that declare their own target with <c>[MapProperty(Target = …)]</c>.
    /// </summary>
    /// <remarks>
    ///     They are not «without a destination»: they have one, and it is not their own name. Judging them
    ///     by name would report PRAG0414 on a write that works — the same reason <c>[MapIgnore]</c> and
    ///     <c>[LinkIds]</c> leave the loop before the comparison. Mapping writes them; where Mapping is not
    ///     referenced the attribute has no effect, and PRAG0445 says so instead of letting the property
    ///     vanish.
    /// </remarks>
    public EquatableArray<string> RetargetedProperties { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     This mutation will have a generated <c>Validate()</c>, because it carries rules of its own.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Predicted, not looked up: the Validation feature emits the validator in the same compilation,
    ///     so the symbol does not expose it yet. It decides whether the call to the mutation's own rules is
    ///     emitted — calling <c>Validate()</c> on a type that will not have it is a compile error inside a
    ///     generated file.
    /// </remarks>
    public bool HasOwnValidator { get; init; }

    /// <summary>
    ///     The compilation references <c>Pragmatic.Validation</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, naming <c>ValidationError</c> in generated code is <c>CS0234</c> in a file the
    ///     author cannot open: generated code can name only what the consumer references. The answer comes
    ///     from the <c>FeatureDetector</c>, as for <c>MappingOwnsTheBody</c>.
    /// </remarks>
    public bool ValidationIsAvailable { get; init; }

    /// <summary>Whether <c>[NoValidation]</c> is on the mutation.</summary>
    public bool HasNoValidation { get; init; }

    /// <summary>Whether sync validation runs, as <c>[Validate]</c> declares it. Default true.</summary>
    public bool RunSync { get; init; } = true;

    /// <summary>Whether async validation runs, as <c>[Validate]</c> declares it.</summary>
    public bool RunAsync { get; init; }

    /// <summary>
    ///     Whether <c>[Validate]</c> is on the mutation. Without it, async validation runs exactly when
    ///     the compilation declares a <c>[Validator]</c> for the mutation.
    /// </summary>
    public bool ValidateIsDeclared { get; init; }

    /// <summary>
    ///     The parent declares that it answers for the permissions of the children it nests.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The default is <c>false</c>, meaning «the child's permission applies»: it is the safe
    ///     behaviour, and silence must not decide in the author's place. With
    ///     <c>[AbsorbsChildPermissions]</c> the invoker does not walk the tree, and whoever holds the
    ///     permission on the parent writes the children too.
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


    public string? BelongsToTypeName { get; init; }

    /// <summary>
    ///     Explicit sub-boundary name for grouping in the boundary interface. When set, the mutation
    ///     is placed in the <c>I{Boundary}{SubBoundary}Actions</c> sub-interface instead of the group
    ///     the namespace infers — the same field <see cref="ActionModel.SubBoundaryName" /> carries.
    /// </summary>
    public string? SubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Name = …)]</c> says, verbatim, or null when it is not written.</summary>
    public string? DeclaredSubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Description = …)]</c> says: the group interface's summary.</summary>
    public string? SubBoundaryDescription { get; init; }
    public bool IsInternal { get; init; }
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public MutationInvalidReason InvalidReason { get; init; } = MutationInvalidReason.None;
    public bool IsValid => InvalidReason == MutationInvalidReason.None;
    public bool HasDependencies => !Dependencies.IsDefaultOrEmpty;

    /// <summary>
    ///     The <c>[LoadEntity]</c> declarations: entities loaded by a key the mutation carries, besides its
    ///     own, after authorization and before <c>ApplyAsync</c> — as on an action.
    /// </summary>
    /// <remarks>
    ///     The attribute compiled on a mutation and nothing read it there: no field, no load, no
    ///     diagnostic.
    /// </remarks>
    public EquatableArray<LoadEntityModel> LoadEntities { get; init; } = EquatableArray<LoadEntityModel>.Empty;

    public EquatableArray<LoadEntityDiagnosticInfo> LoadEntityDiagnostics { get; init; } =
        EquatableArray<LoadEntityDiagnosticInfo>.Empty;

    /// <summary>The <c>[LoadFrom&lt;TQuery&gt;]</c> properties, filled by a declared query before the body.</summary>
    public EquatableArray<LoadFromQueryModel> LoadFromQueries { get; init; } = EquatableArray<LoadFromQueryModel>.Empty;

    public bool HasLoadFromQueries => !LoadFromQueries.IsDefaultOrEmpty;

    /// <summary>The <c>[FromClock]</c> and <c>[FromCurrentUser]</c> properties the invoker writes.</summary>
    public InvokerBindingsModel Bindings { get; init; } = InvokerBindingsModel.None;

    /// <summary>The <c>ValidateLoaded</c> rules the invoker calls after the preload.</summary>
    public LoadedValidationModel LoadedValidation { get; init; } = LoadedValidationModel.None;

    public bool HasLoadEntities => !LoadEntities.IsDefaultOrEmpty;
    public bool HasIncludes => !Includes.IsDefaultOrEmpty;
    public bool HasMappedProperties => !MappedProperties.IsDefaultOrEmpty;

    /// <summary>
    ///     This mutation writes something to the entity, in any of the ways that exist.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The list below must stay complete: the answer decides whether <c>ApplyToEntity</c> is
    ///     emitted, and a mutation without that override inherits the base one, which does nothing. The
    ///     symptom is not an error — it is a success with the data unchanged. Scalars alone are not enough
    ///     (a payload can be children only), and neither are the <c>MappedProperties</c>: a property that
    ///     declares its target with <c>[MapProperty(Target)]</c> does not match by name. The Conformance
    ///     case <c>TheDeclaredTarget</c> covers the second.
    /// </remarks>
    public bool WritesSomething
        => HasMappedProperties
           || HasWritableChildren
           || !RetargetedProperties.IsDefaultOrEmpty
           || !Links.IsDefaultOrEmpty;

    /// <summary>Children the generator can actually write — the rest are reported, not emitted.</summary>
    public bool HasWritableChildren => Children.Any(c => c.IsWritable);

    /// <summary>
    ///     The auto-mapped property the entity governs with a state machine, when there is one (PRAG0434).
    /// </summary>
    /// <remarks>
    ///     Null in the ordinary case. Set, it means the generated mapping will assign a state rather than
    ///     transition to it, which makes the declared transitions decorative for the one writer that
    ///     matters.
    /// </remarks>
    public string? StateMachineMappedProperty { get; init; }
    public EquatableArray<ActionPropertyModel> InputProperties { get; init; } = EquatableArray<ActionPropertyModel>.Empty;
    public bool HasInputProperties => !InputProperties.IsDefaultOrEmpty;

    /// <summary>Domain events declared via <c>[Raises&lt;T&gt;]</c> on the mutation; raised on the entity by the generated invoker.</summary>
    public EquatableArray<RaisedEventModel> RaisedEvents { get; init; } = EquatableArray<RaisedEventModel>.Empty;
    public bool HasRaisedEvents => !RaisedEvents.IsDefaultOrEmpty;

    /// <summary>Aggregate invariants (<c>[Invariant]</c> bool methods) on the target entity, enforced before persist.</summary>
    public EquatableArray<InvariantModel> Invariants { get; init; } = EquatableArray<InvariantModel>.Empty;

    /// <summary>
    ///     The entity's methods that carry <c>[Invariant]</c> and that the invoker cannot call — reported
    ///     as PRAG0463 and enforced nowhere.
    /// </summary>
    /// <remarks>
    ///     They are on the <b>mutation's</b> model because that is where the diagnostics of a write are
    ///     reported, while the location inside each one is the <b>method's</b>: the author has to be sent
    ///     to the rule, not to the operation that would have checked it.
    /// </remarks>
    public EquatableArray<UncallableInvariantModel> UncallableInvariants { get; init; } =
        EquatableArray<UncallableInvariantModel>.Empty;

    /// <summary>
    ///     Whether there is anything to check before persist — the aggregate's own rules, or those of a
    ///     child it merges.
    /// </summary>
    /// <remarks>
    ///     A <c>[PartOf]</c> child's rules count here because this is the only write path it has: the
    ///     invoker that merged it is the only thing in a position to ask.
    /// </remarks>
    public bool HasInvariants => !Invariants.IsDefaultOrEmpty || ChildrenWithInvariants.Count > 0;

    /// <summary>The children this mutation writes that carry a rule of their own.</summary>
    public System.Collections.Generic.IReadOnlyList<MutationChildModel> ChildrenWithInvariants =>
        [.. Children.Where(c => c is { IsWritable: true, HasInvariants: true })];

    /// <summary>
    ///     True when the target entity is a <c>[TemporalRelation]</c> with a MaxActive/overlap constraint
    ///     (so it has a generated <c>ValidateTemporalConstraints</c>). The generated invoker overrides
    ///     <c>CheckTemporalConstraints</c> to enforce it in the pipeline.
    /// </summary>
    public bool HasTemporalConstraints { get; init; }
    public bool IsDelete => Mode == MutationModeValue.Delete;
    public bool IsRestore => Mode == MutationModeValue.Restore;
    public string FullQualifiedName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>Whether the entity has [HasOwner] — OwnerId set automatically on Create.</summary>
    public bool IsOwnedEntity { get; init; }

    /// <summary>
    ///     The constructor the invoker builds with, when the entity declares one.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Chosen by <c>ConstructorAnalyzer</c> — the same selection Mapping's <c>ToEntity</c> uses,
    ///     not a second one. It reads <b>declared</b> constructors, so the choice is observed rather
    ///     than predicted: <c>[MapConstructor]</c> wins, otherwise the one whose parameters match most
    ///     of this mutation's properties, ties going to the simpler.
    /// </remarks>
    public EquatableArray<Mapping.Models.ConstructorParameterModel> ConstructorParameters { get; init; } =
        EquatableArray<Mapping.Models.ConstructorParameterModel>.Empty;

    /// <summary>
    ///     The entity's generated <c>Create()</c> takes no parameters, so the invoker can build through
    ///     the factory instead of the constructor.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Predicted, not observed: the factory belongs to another generator. False means «not
    ///     sure», and the invoker falls back to <c>new</c> — the cost of being wrong is a default not
    ///     applied, never code that does not compile.
    /// </remarks>
    public bool EntityHasParameterlessFactory { get; init; }

    // === Contributions (nullable, opt-in) ===

    /// <summary>Soft-delete contribution from [SoftDelete] on entity.</summary>
    public SoftDeleteContribution? SoftDelete { get; init; }
    public bool HasSoftDelete => SoftDelete is not null;

    /// <summary>
    ///     Whether the entity has the generated nested <c>SoftDeleteFilter</c>, which is what a restore
    ///     lifts by name.
    /// </summary>
    /// <remarks>
    ///     Not read off <see cref="SoftDelete" />: that contribution answers "does this mutation behave
    ///     as a soft delete", which a restore does whatever the entity declares — and the scaffolding in
    ///     <c>ResourceMutationModelBuilder</c> does not build one at all. This is the separate question
    ///     of whether a filter type exists to name, and both builders answer it.
    /// </remarks>
    public bool EntityDeclaresSoftDelete { get; init; }

    /// <summary>Resilience policy contribution from [ResiliencePolicy("name")].</summary>
    public ResilienceContribution? Resilience { get; init; }

    /// <summary>The transition <c>[TransitionsTo]</c> declares, which the invoker performs or checks.</summary>
    public TransitionModel? Transition { get; init; }
    public bool HasResilience => Resilience is not null;

    /// <summary>Computed default contribution from [ComputedDefault] on entity properties.</summary>
    public ComputedDefaultContribution? ComputedDefaults { get; init; }
    public bool HasComputedDefaults => ComputedDefaults is not null;

    /// <summary>Preset contribution from [HasPresets] + [PresetProvider] on entity.</summary>
    public PresetContribution? Presets { get; init; }
    public bool HasPresets => Presets is not null;

    /// <summary>Filter override contribution from [WithoutFilter] and [FilterMode] attributes.</summary>
    public FilterOverrideModel? FilterOverrides { get; init; }
    public bool HasFilterOverrides => FilterOverrides?.HasOverrides == true;

    // === Policy (SG-generated, eliminates reflection in PolicyEvaluationFilter) ===

    /// <summary>Fully-qualified name of the policy type from [RequirePolicy&lt;T&gt;].</summary>
    public string? PolicyTypeFullName { get; init; }
    public bool HasPolicy => PolicyTypeFullName is not null;

    // === Permission (SG-generated, eliminates reflection in PermissionAuthorizationFilter) ===

    /// <summary>Permission names from [RequirePermission] (RequireAll = true).</summary>
    public EquatableArray<string> RequireAllPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Permission names from [RequireAnyPermission] (RequireAll = false).</summary>
    public EquatableArray<string> RequireAnyPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Unresolved [RequirePermission] constant paths, resolved later against the catalog.</summary>
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
    ///     The compensator declared by <c>[UndoWith&lt;T&gt;]</c>, fully qualified, or <c>null</c>. It
    ///     compensates the entity, which is what a mutation returns.
    /// </summary>
    public string? CompensatorTypeName { get; init; }

    /// <summary>
    ///     The <c>[CommitStrategy]</c> declared on the type, or <c>null</c> for the default. Written as
    ///     the enum member name so the template can emit it without knowing the enum's values.
    /// </summary>
    public string? CommitMode { get; init; }

    /// <summary>The compensator was declared but does not compensate this mutation's entity (PRAG0425).</summary>
    public string? MismatchedCompensator { get; init; }

    /// <summary>More than one store commits inside one invocation, with no transaction spanning them.</summary>
    public int CommitScopeCount =>
        (WritesOwnStore ? 1 : 0) + (ForeignBoundaryFacades.IsDefaultOrEmpty ? 0 : ForeignBoundaryFacades.Count);

    // === Auto-derived permissions (opt-in, see ActionsFeature.AutoDerive) ===

    /// <summary>Overrides the auto-derived name — <c>[ExplicitPermission]</c> in any of its shapes.</summary>
    public ExplicitPermissionModel? ExplicitPermission { get; init; }

    /// <summary><c>[AllowAnonymous]</c>: no permission at all, not even a derived one.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary><c>"auto-derived"</c>, <c>"explicit"</c>, or null for a hand-written requirement.</summary>
    public string? PermissionSource { get; init; }
}

internal enum MutationInvalidReason
{
    None,
    NotPartial,
    NoBaseType,
    ModeNotDetermined
}

internal enum MutationModeValue
{
    Create = 1,
    Update = 2,
    CreateOrUpdate = 3,
    Delete = 4,
    Restore = 8
}

/// <summary>What is wrong with a mutation's id property, when anything is (PRAG0435).</summary>
internal enum MutationIdProblem
{
    None,
    Missing,
    WrongType,
}

internal enum MutationReturnTypeValue
{
    Id = 0,
    LogicalKey = 1,
    Entity = 2
}

/// <summary>
///     Represents a navigation property target for cascade soft-delete.
/// </summary>
internal sealed record SoftDeleteCascadeTargetModel
{
    /// <summary>Navigation property name on the parent entity (e.g., "Rooms").</summary>
    public required string PropertyName { get; init; }

    /// <summary>Whether this is a collection navigation (OneToMany) vs single reference.</summary>
    public bool IsCollection { get; init; }
}

internal sealed record MutationPropertyMapModel
{
    public required string MutationPropertyName { get; init; }
    public required string MutationPropertyTypeName { get; init; }
    public required string EntityPropertyName { get; init; }
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether the entity's property is written directly rather than through a generated setter.
    /// </summary>
    /// <remarks>
    ///     <c>Set{Name}</c> is emitted only for properties whose setter is <b>not</b> public — that is
    ///     what the change-tracking wrapper is for. The auto-map called it unconditionally, so an
    ///     entity written in the ordinary C# way, with <c>{ get; set; }</c>, produced a call to a
    ///     method nobody generates: a build error inside a file its author cannot open. It never
    ///     surfaced because the reference application and every fixture use <c>private set</c>.
    /// </remarks>
    public bool EntityHasPublicSetter { get; init; }

    /// <summary>
    ///     The test that says this property's value can be converted, when the conversion can fail.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Empty for the overwhelming majority: only a <c>string</c> that has to become a number,
    ///         a date, a <c>Guid</c>, a <c>bool</c> or an enum can fail on the value. Those go through
    ///         a <c>Parse</c>, and a <c>Parse</c> throws where nothing catches — so the caller was
    ///         told 500 for a body they sent.
    ///     </para>
    ///     <para>
    ///         Carried on the model rather than worked out in the template because the transform is
    ///         the only place that holds both symbols: by template time the entity's property type is
    ///         gone.
    ///     </para>
    /// </remarks>
    public string? CanConvertCheck { get; init; }

    /// <summary>What the caller is told the value should have been, when the check fails.</summary>
    public string? ExpectedShape { get; init; }
}

/// <summary>
///     Represents a mutation property that could not be matched to an entity setter.
/// </summary>
internal sealed record UnmappedMutationPropertyModel
{
    /// <summary>The property name on the mutation class.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The entity type name (short name for diagnostics).</summary>
    public required string EntityTypeName { get; init; }
}
