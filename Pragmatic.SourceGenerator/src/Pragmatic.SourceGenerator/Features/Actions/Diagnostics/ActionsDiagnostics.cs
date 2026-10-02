using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the Actions feature.
///     Diagnostic IDs: PRAG0400-0449
/// </summary>
internal static partial class ActionsDiagnostics
{
    private const string Category = "Pragmatic.Actions";

    // PRAG0400 (action class must be partial) and PRAG0406 ([Boundary] must be partial) are the companion
    // analyzer's, which reports them on the declaration (NotPartialDiagnosticDescriptors); the generator
    // skips the type silently.

    public static readonly DiagnosticDescriptor MustInheritFromDomainAction = new(
        "PRAG0401",
        "Action class must inherit from DomainAction base class",
        "Action class '{0}' must inherit from DomainAction<T> or VoidDomainAction",
        Category, DiagnosticSeverity.Error, true);

    // PRAG0402 is not assigned: an action without injected dependencies is perfectly valid, so there is
    // nothing to say about it. Do not reuse the ID.

    public static readonly DiagnosticDescriptor LoadEntityIdPropertyNotFound = new(
        "PRAG0404",
        "[LoadEntity] or [LoadEntities] key property not found",
        "[{3}<{0}>] on '{1}': property '{2}' was not found on '{1}'",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor LoadEntityKeyTypeNotFound = new(
        "PRAG0405",
        "[LoadEntity] or [LoadEntities] could not determine entity key type",
        "[{2}<{0}>] on '{1}': could not determine the key type for entity '{0}'",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     A <c>[LoadCurrentUser]</c> the invoker cannot load: the reason is the last argument.
    /// </summary>
    /// <remarks>
    ///     Outside the 0400–0449 range, as <c>PRAG0450</c> is: that range is spent, and the ids left in it
    ///     belong to removed diagnostics a test still names.
    /// </remarks>
    public static readonly DiagnosticDescriptor CurrentUserCannotBeLoaded = new(
        "PRAG0451",
        "[LoadCurrentUser] cannot be generated",
        "[LoadCurrentUser] on '{0}' cannot be generated: {1}",
        Category, DiagnosticSeverity.Error, true,
        "The invoker loads the signed-in user's entity through the generated {User}Resolver: the module needs "
        + "exactly one [PragmaticUser] entity, and Pragmatic.Identity.Persistence referenced for its resolver.");

    /// <summary>
    ///     An <c>Include</c> path of a <c>[LoadEntity]</c>, or an <c>[EagerLoad]</c> path of a mutation, with a segment
    ///     that is not a navigation of the entity. The path is not emitted.
    /// </summary>
    public static readonly DiagnosticDescriptor LoadEntityIncludeNotANavigation = new(
        "PRAG0453",
        "An include path names no navigation",
        "[{4}] on '{1}': '{2}' — '{3}' is not a navigation of the entity it is read on",
        Category, DiagnosticSeverity.Error, true,
        "An Include path is a chain of navigations — declared, or generated from a [Relation] — each read on the "
        + "entity the previous one leads to. A segment that names nothing, or names a scalar, would be refused by "
        + "EF Core at the first request.");

    /// <summary>
    ///     A load's <c>Specification</c> that names no static <c>Specification&lt;TEntity&gt;</c> of the entity.
    /// </summary>
    public static readonly DiagnosticDescriptor LoadSpecificationNotFound = new(
        "PRAG0454",
        "[LoadEntity] or [LoadEntities] Specification names no specification of the entity",
        "[{2}<{0}>] on '{1}': Specification '{3}' — {4}",
        Category, DiagnosticSeverity.Error, true,
        "The rule is a static method, property or field returning a Specification<TEntity> — named with "
        + "nameof(EntitySpecifications.Member), or by the bare name of a member of {Entity}Specifications.");

    /// <summary>A parameter of a load's rule that binds no property of the operation.</summary>
    public static readonly DiagnosticDescriptor LoadSpecificationParameterUnbound = new(
        "PRAG0455",
        "A parameter of the load's specification binds no property",
        "[{2}<{0}>] on '{1}': {3}",
        Category, DiagnosticSeverity.Error, true,
        "Each parameter of the rule is bound by name, ignoring case, to a property of the operation whose "
        + "value converts to it; an optional parameter with no property of its name keeps its default.");

    /// <summary>A load that names both a key and a <c>Specification</c>, or neither.</summary>
    public static readonly DiagnosticDescriptor LoadEntityKeyOrSpecification = new(
        "PRAG0456",
        "A load names a key or a specification, not both",
        "[{2}<{0}>] on '{1}': name the key property or a Specification — {3}",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     <c>RequireReadPermission</c> on a load of an entity the permission catalogue has no read permission
    ///     for.
    /// </summary>
    /// <remarks>
    ///     A build error rather than a check that asks nothing: an operation that declared the caller must
    ///     be able to read the rows would otherwise answer anyone.
    /// </remarks>
    public static readonly DiagnosticDescriptor LoadReadPermissionUnknown = new(
        "PRAG0457",
        "RequireReadPermission on an entity with no known read permission",
        "[{2}<{0}>] on '{1}': RequireReadPermission asks the read permission of '{0}', and none is known — the entity's permissions are generated with it, by the persistence generator, here or in the module that owns it",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     A <c>[LoadFrom&lt;TQuery&gt;]</c> the invoker cannot fill: the query is no declared query of this
    ///     compilation, or the property is not of what it answers.
    /// </summary>
    public static readonly DiagnosticDescriptor LoadFromNotTheQuerysAnswer = new(
        "PRAG0458",
        "[LoadFrom] property cannot hold the query's result",
        "[LoadFrom<{0}>] on '{1}.{2}': {3}",
        Category, DiagnosticSeverity.Error, true,
        "The property is of what the query answers: the result for a Single query, PagedResult<TResult> for a "
        + "paged one, IReadOnlyList<TResult> otherwise — and TQuery is a [Query] declared in this compilation.");

    /// <summary>An input of a <c>[LoadFrom]</c> query that binds no property of the operation.</summary>
    public static readonly DiagnosticDescriptor LoadFromInputUnbound = new(
        "PRAG0459",
        "An input of the [LoadFrom] query binds no property",
        "[{2}<{0}>] on '{1}': {3}",
        Category, DiagnosticSeverity.Error, true,
        "Each input of the query is bound by name, ignoring case, from a property of the operation whose value "
        + "converts to it; an input that is not required and has no property keeps its default.");

    /// <summary>
    ///     A <c>[LoadEntity(By = …)]</c> that names no single-part <c>[LogicKey]</c> of the entity, or stands beside a
    ///     <c>Specification</c>.
    /// </summary>
    /// <remarks>Reported rather than read by id: the row the operation asked for is the one its domain key names.</remarks>
    public static readonly DiagnosticDescriptor LoadByNotTheLogicKey = new(
        "PRAG0460",
        "[LoadEntity] By names no logic key of the entity",
        "[{2}<{0}>] on '{1}' cannot load by the logic key: {3}",
        Category, DiagnosticSeverity.Error, true,
        "By names the entity's [LogicKey] member — one part — and goes with the key property that holds its value: "
        + "[LoadEntity<Employee>(nameof(EmployeeNumber), By = nameof(Employee.EmployeeNumber))].");

    /// <summary>A <c>[LoadEntity(By = …)]</c> whose key property is not of the logic key's type.</summary>
    public static readonly DiagnosticDescriptor LoadByKeyTypeMismatch = new(
        "PRAG0461",
        "[LoadEntity] By key property is not of the logic key's type",
        "[{2}<{0}>] on '{1}' loads by the logic key '{3}', of type '{4}', but '{5}' is '{6}' — declare it '{4}'",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>A <c>[RequireExists]</c> a <c>[LoadEntity]</c> of the same entity and key already proves.</summary>
    /// <remarks>A warning: only the load runs, so nothing breaks — the declaration is redundant, not wrong.</remarks>
    public static readonly DiagnosticDescriptor RequireExistsBesideALoad = new(
        "PRAG0462",
        "[RequireExists] beside a [LoadEntity] of the same key",
        "[RequireExists<{0}>(nameof({2}))] on '{1}' is proved by the [LoadEntity<{0}>(nameof({2}))] beside it — a row that was read exists; only the load runs",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>
    ///     A <c>ValidateLoaded</c> / <c>ValidateLoadedAsync</c> the invoker would not call: the name, and not the
    ///     signature.
    /// </summary>
    public static readonly DiagnosticDescriptor LoadedValidationMisshapen = new(
        "PRAG0452",
        "ValidateLoaded has a shape the invoker does not call",
        "'{0}.{1}' is not called by the invoker: declare 'ValidationError ValidateLoaded()' or 'Task<ValidationError> ValidateLoadedAsync(CancellationToken)' — an instance method, not generic",
        Category, DiagnosticSeverity.Error, true,
        "The invoker calls the operation's ValidateLoaded rules right after the preload, before the body. A method "
        + "with the name and another signature reads like a rule and would never run.");

    /// <summary>
    ///     The key property of a <c>[LoadEntity]</c> is not of the entity's key type — nor its nullable
    ///     form, which is the optional load — or that of a <c>[LoadEntities]</c> not a collection of it.
    /// </summary>
    /// <remarks>
    ///     Otherwise only the compiler would find it: <c>GetByIdAsync(op.Key)</c> is CS1503 inside the
    ///     generated invoker, a file the author cannot open, pointing at nothing they wrote.
    /// </remarks>
    public static readonly DiagnosticDescriptor LoadEntityKeyTypeMismatch = new(
        "PRAG0411",
        "[LoadEntity] or [LoadEntities] key is not of the entity's key type",
        "[{5}<{0}>] on '{1}': property '{2}' is '{3}', and '{0}' is keyed by '{4}' — {6}",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor BoundaryNoNamespace = new(
        "PRAG0407",
        "[Boundary] class must be in a namespace",
        "[Boundary] class '{0}' must be in a namespace",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MutationMustInheritFromBase = new(
        "PRAG0409",
        "Mutation class must inherit from Mutation<TEntity>",
        "Mutation class '{0}' must inherit from Mutation<TEntity>",
        Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor MutationModeNotDetermined = new(
        "PRAG0410",
        "Mutation mode could not be determined",
        "Mutation class '{0}': specify [Mutation(Mode = ...)] or use Create/Update prefix in the class name",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // SubBoundary diagnostics (PRAG0412-0413)
    // =========================================================================

    public static readonly DiagnosticDescriptor SubBoundaryNestingTooDeep = new(
        "PRAG0412",
        "SubBoundary nesting deeper than 2 levels",
        "SubBoundary '{0}' in boundary '{1}' has {2} nesting levels — consider flattening",
        Category, DiagnosticSeverity.Warning, true);

    // The one signal of the point where a folder becomes public API without anyone writing it. On by
    // default and reported on the operation: off, or with no location, it would be equal to the silence
    // it is meant to break, found at the top of the log rather than on the operation.
    public static readonly DiagnosticDescriptor SubBoundaryInferred = new(
        "PRAG0413",
        "SubBoundary inferred from namespace",
        "SubBoundary '{0}' was inferred from namespace structure in boundary '{1}'",
        Category, DiagnosticSeverity.Info, true);

    /// <summary>
    ///     PRAG0416: a <c>[SubBoundary(Name = …)]</c> that cannot be a group.
    /// </summary>
    /// <remarks>
    ///     The rules for an inferred group are rules about the group, not about how it was arrived
    ///     at: never the boundary's own name — which would generate <c>ISalesSalesActions</c> — and
    ///     never nothing, which would fall back to the namespace and make the declaration a no-op. A
    ///     declaration that silently does nothing is the one outcome the attribute must not have.
    /// </remarks>
    public static readonly DiagnosticDescriptor SubBoundaryNameCannotBeAGroup = new(
        "PRAG0416",
        "[SubBoundary] names no group",
        "[SubBoundary(Name = \"{0}\")] on '{1}': {2}",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    /// <summary>
    ///     PRAG0446: the entity's constructor asks for a value the mutation does not carry.
    /// </summary>
    /// <remarks>
    ///     The invoker passes constructor arguments by position. Writing <c>default</c> for a parameter
    ///     it cannot match would be a create that silently builds an entity with a null where the
    ///     constructor demands a value, so the case is refused by this diagnostic instead.
    ///     A parameter with a default of its own is a different case: it is simply omitted.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationMissesAConstructorArgument = new(
        "PRAG0446",
        "The entity's constructor needs a value the mutation does not carry",
        "'{0}' creates '{1}', whose constructor requires '{2}', and carries no property of that name. "
        + "The value would be filled with default. Add a '{2}' property to the mutation, give the "
        + "parameter a default of its own, or construct through a factory.",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     PRAG0445: <c>[MapProperty]</c> on a mutation, without Pragmatic.Mapping referenced.
    /// </summary>
    /// <remarks>
    ///     A mutation is a mapping under another classifier, so its body is Mapping's wherever
    ///     Mapping is referenced — and only there is a declared target read. Without it the attribute
    ///     compiles, matches nothing, and the property is written nowhere: silence at the one place
    ///     the author was most explicit about intent.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationRetargetNeedsMapping = new(
        "PRAG0445",
        "[MapProperty] on a mutation needs Pragmatic.Mapping",
        "Property '{0}' on '{1}' declares a target with [MapProperty], but Pragmatic.Mapping is not "
        + "referenced. The target is not read and the property is written nowhere: reference "
        + "Pragmatic.Mapping, or rename the property to match the entity member.",
        "Pragmatic.Actions",
        DiagnosticSeverity.Warning,
        true);

    // Mutation mapping diagnostics (PRAG0414)
    // =========================================================================

    public static readonly DiagnosticDescriptor MutationPropertyNoMatchingSetter = new(
        "PRAG0414",
        "Mutation property has no matching setter on entity",
        "Mutation property '{0}' has no matching setter 'Set{0}()' on entity '{1}'. Add a Set{0}() method to the entity or use [MapIgnore] to skip this property.",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>PRAG0435: a mutation that loads a row has no usable id property.</summary>
    /// <remarks>
    ///     Create makes a row; every other mode finds one, and the invoker finds it by the mutation's
    ///     <c>Id</c>. Without one the generated <c>LoadEntityAsync</c> is
    ///     <c>Task.FromResult&lt;TEntity?&gt;(null)</c>: it compiles, it ships, and the operation matches
    ///     nothing on every call — with a clean build and nothing to read. The type is checked for the
    ///     same reason, since an id of the wrong type either breaks inside a generated file the author
    ///     cannot open, or converts quietly and matches no row.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationIdPropertyUnusable = new(
        "PRAG0435",
        "Mutation cannot address the row it operates on",
        "Mutation '{0}' is Mode = {1}, which loads an existing {2}, but {3}. Declare "
        + "'public required {4} Id {{ get; init; }}' on it.",
        Category, DiagnosticSeverity.Error, true,
        "A mutation that loads an entity needs an id of the entity's key type to load it by.");

    /// <summary>PRAG0434: a mutation auto-maps a property the entity governs with a state machine.</summary>
    /// <remarks>
    ///     <para>
    ///         Auto-mapping emits <c>Set{Property}(this.{Property})</c>, which assigns the state and never
    ///         asks whether the move is legal. The transitions declared with <c>[TransitionFrom]</c> are
    ///         generated, correct, and bypassed by the writer — the rule holds everywhere except where
    ///         the state actually changes.
    ///     </para>
    ///     <para>
    ///         A warning rather than an error, because assigning is sometimes what is meant: a migration,
    ///         an administrative correction, a repair. Those say so by suppressing it. There is no
    ///         attribute to opt in with, and deliberately — one more marker for a case this rare is a
    ///         second mechanism for something suppression already expresses.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationAssignsStateMachineProperty = new(
        "PRAG0434",
        "Mutation assigns a state-machine property instead of transitioning",
        "Mutation '{0}' auto-maps '{1}', which '{2}' governs with a state machine — the assignment "
        + "bypasses the declared transitions. Mark it [MapIgnore] and declare the move with "
        + "[TransitionsTo<TState>(target)] on the mutation, or suppress this if assigning is what you mean.",
        Category, DiagnosticSeverity.Warning, true);

    /// <summary>PRAG0436: a mutation carries children of an entity that has a life of its own.</summary>
    /// <remarks>
    ///     <para>
    ///         Writing a child from its parent's operation writes a row past whatever permissions,
    ///         validation and events that row's own operations would have applied. In the reference
    ///         application <c>Property</c> and <c>Invoice</c> both declare
    ///         <c>[Relation.OneToMany]</c>, but a room type has its own mutations and its own
    ///         <c>RoomType.Update</c> permission while a line item has neither — the relation cannot
    ///         tell them apart, and guessing would have opened the parent as a way around the child's
    ///         permission.
    ///     </para>
    ///     <para>
    ///         An error, and fail-closed: the child says it has no life of its own with
    ///         <c>[PartOf&lt;TParent&gt;]</c>, or it is written through its own operations.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationChildIsNotPartOfTheAggregate = new(
        "PRAG0436",
        "Mutation writes an entity that is not part of its aggregate",
        "Mutation '{0}' carries '{1}', which writes '{2}' — but '{2}' is not declared part of "
        + "'{3}'. Add [PartOf<{3}>] to '{2}' if it has no life of its own, or write it through its "
        + "own mutation, composed with this one.",
        Category, DiagnosticSeverity.Error, true,
        "Writing a child from its parent skips the permissions, validation and events of the child's own operations.");

    /// <summary>PRAG0437: a mutation's child collection has nothing to match its elements by.</summary>
    /// <remarks>
    ///     The same condition as PRAG0333 on the mapping side, reported here because this is the
    ///     generator that emits the merge. Without a key every incoming element looks new, and the
    ///     children that are already there would be removed and rebuilt.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationChildElementsCannotBeMatched = new(
        "PRAG0437",
        "Mutation child elements cannot be matched",
        "Mutation '{0}' carries '{1}', whose elements have no key in common with '{2}'. Give the "
        + "element DTO an Id, or the child entity a [LogicKey] the DTO also carries, or declare "
        + "[CollectionStrategy(CollectionStrategy.Replace)] if rebuilding the collection is intended.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>PRAG0439: a mutation carries children the entity has nowhere to put.</summary>
    /// <remarks>
    ///     The silent case: unreported, the child is skipped, the operation compiles, the endpoint
    ///     answers 200, and the whole set comes back unchanged. A child with no navigation to be written to is a name that matches nothing, and
    ///     saying nothing about it is the shape this codebase keeps paying for.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationChildHasNoNavigation = new(
        "PRAG0439",
        "Mutation carries children the entity cannot hold",
        "Mutation '{0}' carries '{1}', but '{2}' has no navigation called '{1}' — declare the relation "
        + "with [Relation.OneToMany<{3}>] on '{2}', or name the property after the navigation it writes.",
        Category, DiagnosticSeverity.Error, true,
        "A carried child is written to a navigation; without one there is nowhere for it to go.");

    /// <summary>PRAG0443: the child writes one entity and the navigation holds another.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>[PartOf]</c> answers "may this parent write it", not "is this the navigation it goes
    ///         into". Two children of the same aggregate both pass the first question, so without this
    ///         check swapping them reaches the template — which assigns one entity type into a
    ///         collection of another and produces <c>CS0411</c> inside a generated file the author
    ///         cannot open. Measured: <c>Allocation</c> under the <c>Lines</c> navigation.
    ///     </para>
    ///     <para>
    ///         The two questions are separate on purpose: the declared parent is about ownership, the
    ///         navigation about where the rows live. Checking only the first is what let the mismatch
    ///         through.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationChildDoesNotMatchTheNavigation = new(
        "PRAG0443",
        "The child writes an entity the navigation does not hold",
        "Mutation '{0}' carries '{1}', which writes '{2}', but the navigation '{3}' on '{4}' holds "
        + "'{5}'. Carry the mutation that writes '{5}', or name the navigation that holds '{2}'.",
        Category, DiagnosticSeverity.Error, true,
        "A child is written into the navigation that holds its own entity type.");

    /// <summary>PRAG0444: a mutation writes a child that belongs to another boundary.</summary>
    /// <remarks>
    ///     <para>
    ///         A boundary is the transaction boundary: each one saves from its own <c>DbContext</c>.
    ///         A nested write reaching across one is committed by the <b>parent's</b> unit of work,
    ///         past every rule the owning boundary states about its own rows. The <c>DbSet</c> that
    ///         <c>[ReadAccess&lt;T&gt;]</c> adds is writable like any other, because
    ///         <c>ExcludeFromMigrations</c> is about the schema, not about permissions; the boundary's
    ///         <c>SaveChanges</c> refuses the commit (<c>ReadAccessAcrossTheBoundary</c>), and this
    ///         diagnostic refuses the shape before it compiles.
    ///     </para>
    ///     <para>
    ///         The relation itself is legitimate — an <c>Order</c> may point at a <c>CatalogItem</c>.
    ///         What this rejects is <b>writing</b> through it: read across the boundary, and let the
    ///         other side write its own rows in answer to a domain event.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationChildCrossesABoundary = new(
        "PRAG0444",
        "A mutation writes a child of another boundary",
        "Mutation '{0}' carries '{1}', which writes '{2}' of boundary '{3}'. '{4}' belongs to '{5}', "
        + "and a boundary is a transaction boundary: this child would be committed by the wrong unit "
        + "of work, past the rules '{3}' states about its own rows. Read across the boundary and "
        + "raise a domain event for the write.",
        Category, DiagnosticSeverity.Error, true,
        "Nesting a write stays inside one boundary.");

    /// <summary>PRAG0442: a mutation carries a DTO child instead of a child mutation.</summary>
    /// <remarks>
    ///     <para>
    ///         A child of an aggregate is written through a <b>mutation</b>, never through a DTO. A DTO
    ///         is shape without behaviour: it has no <c>ApplyAsync</c>, no validators of its own and no
    ///         <c>[RequirePermission]</c>, so writing a child through one is a way around every rule the
    ///         child would have applied.
    ///     </para>
    ///     <para>
    ///         A mutation needs no mapping attribute to be nested: <c>ApplyToEntity</c> is virtual on
    ///         <c>Mutation&lt;TEntity&gt;</c>, and a mutation exposed as an endpoint is its own request
    ///         shape — there is no second type to declare.
    ///     </para>
    ///     <para>
    ///         ⚠️ The rule is about nesting <b>inside a mutation</b>. Between plain objects, a DTO that
    ///         nests another DTO is the correct and final form: mapping neither has nor wants this
    ///         constraint.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationCarriesADtoChild = new(
        "PRAG0442",
        "A mutation writes its children through mutations, not DTOs",
        "Mutation '{0}' carries '{1}' of type '{2}', which is a DTO. A child of an aggregate is written "
        + "through a mutation of its own, so that its validation and permissions run: declare a "
        + "[Mutation] on '{3}' and carry that instead. A mutation needs no mapping attribute — it is "
        + "its own request shape.",
        Category, DiagnosticSeverity.Error, true,
        "Nesting inside a mutation is limited to mutations, along a declared relation.");

    /// <summary>PRAG0438: an entity declared part of an aggregate also has an operation of its own.</summary>
    /// <remarks>
    ///     <c>[PartOf&lt;TParent&gt;]</c> means "written through the parent". A mutation targeting the
    ///     child says the opposite, and both are the author's own words — so this is a contradiction
    ///     between two declarations, not a mistake the generator can resolve.
    /// </remarks>
    public static readonly DiagnosticDescriptor MutationTargetsAChildOfAnAggregate = new(
        "PRAG0438",
        "Mutation targets an entity declared part of another aggregate",
        "Mutation '{0}' targets '{1}', which declares [PartOf<{2}>] — it is written through '{2}'. "
        + "Remove [PartOf<{2}>] from '{1}' if it has operations of its own, or remove this mutation.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // Policy registry diagnostics (PRAG0415)
    // =========================================================================

    public static readonly DiagnosticDescriptor PolicyTypeUnresolved = new(
        "PRAG0415",
        "Policy type could not be resolved",
        "Action/mutation '{0}' declares [AuthorizationPolicy] but the policy type could not be resolved — ensure the referenced type exists and is accessible.",
        Category, DiagnosticSeverity.Warning, true);

    // No diagnostic rejects [RequirePolicy<T>] on a mutation: mutations evaluate it in
    // MutationInvoker.CheckPolicyAndResourceAsync (permission → policy → resource).

    // PRAG0417 (MutationReturnTypeNotImplemented) is retired: Id, LogicalKey and Entity are all
    // wired — the boundary member returns what the mutation declares and the endpoint answers it.

    // =========================================================================
    // Mutation return-type diagnostics (PRAG0403)
    // =========================================================================

    /// <summary>
    ///     PRAG0403: a <c>ReturnType = LogicalKey</c> mutation whose key the generator cannot type.
    /// </summary>
    /// <remarks>
    ///     Two ways to get here: the entity declares no <c>[LogicKey]</c>, so there is nothing to
    ///     return; or a part of it is a key a relation declared on the other entity puts on this one,
    ///     whose type only the relation graph knows. An error, because the record the mutation would
    ///     return needs a type for every part, and guessing one is a compiler error in a file the author
    ///     cannot open.
    /// </remarks>
    public static readonly DiagnosticDescriptor LogicalKeyCannotBeReturned = new(
        "PRAG0403",
        "The mutation cannot return its logical key",
        "Mutation '{0}' declares ReturnType = LogicalKey, but {1}. Return the Id or the entity instead, or "
        + "declare the key's parts on properties the entity has.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // Permission constant resolution (PRAG0418)
    // =========================================================================

    // [RequirePermission(GeneratedConst)] whose value the generator could not resolve (and which is not a
    // known generated entity permission) would NOT be enforced. The generator cannot refuse it, so it
    // says so: a warning, so the fail-open is at least not silent.
    public static readonly DiagnosticDescriptor PermissionConstNotResolved = new(
        "PRAG0418",
        "Permission constant could not be resolved",
        "'{0}' declares [RequirePermission({1})] but that constant could not be resolved to a permission value — it would NOT be enforced (fail-open). Use a literal permission string, or ensure it is a generated entity permission.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Dependency classification (PRAG0419)
    // =========================================================================

    // A concrete reference type carries no signal saying whether it is an injected service or plain
    // state, so the generator cannot decide. It skips the field (treats it as state) and says so
    // instead of silently dropping a dependency that would then be null at runtime.
    public static readonly DiagnosticDescriptor DependencyTypeAmbiguous = new(
        "PRAG0419",
        "Cannot decide whether the field is an injected dependency",
        "Field '{0}' on '{1}' has concrete type '{2}': the generator cannot tell an injected service from plain state, so the field is NOT injected. Depend on an interface or abstract type, or mark '{2}' with [Service].",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Resilience policy name (PRAG0420)
    // =========================================================================

    // An empty/whitespace [ResiliencePolicy("")] would flow all the way into the generated invoker,
    // which would then look up a policy that cannot exist. Reject it at compile time.
    public static readonly DiagnosticDescriptor ResiliencePolicyNameEmpty = new(
        "PRAG0420",
        "Resilience policy name is empty",
        "'{0}' declares [ResiliencePolicy] with an empty or whitespace-only name — no such policy can be registered, so the attribute is ignored. Provide the policy name.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Explicit permission resolution (PRAG0421)
    // =========================================================================

    // [ExplicitPermission(Constant)] names a constant the catalogue must know: one this compilation does not
    // generate cannot be read — the operation then falls back to its derived name, which is safe but is not
    // what the author asked for. Say so.
    public static readonly DiagnosticDescriptor ExplicitPermissionNotResolved = new(
        "PRAG0421",
        "Explicit permission could not be resolved",
        "'{0}' declares [ExplicitPermission] referring to '{1}', which is not in this compilation's permission catalog — the auto-derived permission is used instead. Name a constant this assembly generates (an entity's CRUD, or an [assembly: Permission]), or pass the permission name as a string.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Nested action (PRAG0408)
    // =========================================================================

    // The generated partial does not reproduce the containing-type chain: for an action
    // nested in another type it declares a namespace-level class of the same simple name, so the
    // partial does not extend the user's type at all and the invoker inside it cannot override a base
    // generic over a type it is not. What the author sees is CS0534 and CS0115 pointing at a file they
    // did not write. Error, not warning: the compilation fails either way, and this is the only message
    // that names the cause.
    public static readonly DiagnosticDescriptor ActionMustBeTopLevel = new(
        "PRAG0408",
        "Action must be declared at namespace level",
        "'{0}' is declared inside '{1}'. Move it to namespace level: the generated invoker cannot extend a nested type, and leaving it here fails the build with compiler errors in generated code.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // Empty permission requirement (PRAG0422)
    // =========================================================================

    // The attribute constructor already rejects an empty list — "an empty permission set
    // would silently grant access" — but the runtime is zero-reflection, so for an action that
    // constructor never runs. The generator extracts no permission, the type never enters the
    // requirement registry, and the filter reads the absence as "nothing was required". The two cases
    // are indistinguishable by then, so the rule is enforced here, where the declaration still exists.
    // Error, not warning: a warning would leave the action running unprotected, which is the defect.
    public static readonly DiagnosticDescriptor EmptyPermissionRequirement = new(
        "PRAG0422",
        "Permission requirement declares no permissions",
        "'{0}' declares [{1}] with no permissions. It would NOT be enforced: nothing reaches the generated permission registry, and the pipeline treats the action as having no requirement at all (fail-open). Name at least one permission, or remove the attribute.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // Delegation subject does not resolve (PRAG0423)
    // =========================================================================

    // [StartsDelegation] names the property holding the subject's id. If that property is missing or
    // is not a string, the generator has nothing to read and emits no scope — so the action runs as
    // the caller while its declaration says it runs for someone else. Error, not warning: silently
    // dropping the scope is the fail-open the attribute exists to prevent, and the alternative
    // (emitting a scope over an empty string) throws from ActAs at runtime, inside a domain action.
    public static readonly DiagnosticDescriptor DelegationSubjectNotFound = new(
        "PRAG0423",
        "Delegation subject property not found",
        "'{0}' declares [StartsDelegation(\"{1}\")] but has no string property named '{1}'. The delegation scope cannot be generated, and the action would run as the caller while claiming to act for someone else. Use nameof() on a string property of the action.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // More than one commit scope, no decision recorded (PRAG0424)
    // =========================================================================

    // A boundary is the transaction boundary — that is the design decision this reports against, not a
    // limitation. Each one saves through its own DbContext, so an action that writes in its own and
    // calls another commits twice, inner first, with nothing to roll the inner commit back when the
    // outer step fails — the inner rows survive, pointing at a parent row that was never written.
    //
    // Warning, not error, because the right answer depends on the domain. The order of the three is
    // not arbitrary: a domain event is how work crosses a boundary without pretending to be atomic;
    // [UndoWith<T>] is the deliberate exception, best-effort and in-request, with the crash window
    // stated on the attribute; [AcceptsPartialWrites] accepts the leftovers and says why.
    public static readonly DiagnosticDescriptor UnrecordedPartialWriteRisk = new(
        "PRAG0424",
        "Work crosses a boundary inside one transaction",
        "'{0}' writes in its own boundary and calls another within one invocation ({1}). A boundary is a transaction boundary: each saves separately, inner first, so a failure after that point leaves the inner writes committed. Raise a domain event and let the other boundary react; or declare [UndoWith<T>] on the step to undo it (best effort, in-request); or record [AcceptsPartialWrites(\"reason\")] if the leftovers are harmless.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Declared compensator does not compensate this action (PRAG0425)
    // =========================================================================

    // [UndoWith<T>] cannot constrain T to ICompensates<TReturn>: the attribute does not know the
    // action's return type. So the constraint is checked here, and it has to be an error — a mismatched
    // compensator generates nothing, and the action would go on declaring an undo that never runs while
    // PRAG0424 stays quiet because a decision appears to have been recorded.
    public static readonly DiagnosticDescriptor CompensatorDoesNotMatch = new(
        "PRAG0425",
        "Compensator does not compensate this action",
        "'{0}' declares [UndoWith<{1}>], but '{1}' does not implement {2}. No compensation would be generated, and the declaration would silence PRAG0424 without undoing anything.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // A transaction cannot reach into another boundary (PRAG0426)
    // =========================================================================

    // The promise [Transactional] makes is "everything here is one transaction". A call into another
    // boundary breaks it by construction: those writes go through a unit of work this invoker does not
    // hold, so its rollback cannot reach them. Error, not warning — a declared guarantee that silently
    // covers only part of the work is worse than no guarantee, because the reader stops checking.
    public static readonly DiagnosticDescriptor TransactionCrossesBoundary = new(
        "PRAG0426",
        "A transactional action calls another boundary",
        "'{0}' is [Transactional] but calls {1}. A boundary is a transaction boundary: those writes commit through their own unit of work and a rollback here cannot undo them. Raise a domain event and let the other boundary react, or drop [Transactional] and answer PRAG0424 instead.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // A composite with no steps (PRAG0427)
    // =========================================================================

    // [CompositeAction] orchestrates step properties, and the documented convention is that the body is
    // never invoked — so a composite whose properties are none of mutation, action or void action gets
    // no generated invoker and runs an empty Execute. It does nothing, successfully, and without this
    // diagnostic nothing would say so.
    //
    // Warning rather than error: an action that declares no steps and does all its work in Execute is
    // correct code with a decorative attribute, and failing that build would be wrong — what is wrong
    // is that nothing says the attribute means nothing.
    public static readonly DiagnosticDescriptor CompositeHasNoSteps = new(
        "PRAG0427",
        "Composite action has no steps",
        "'{0}' is [CompositeAction] but declares no step properties. A step is a property whose type is a Mutation<T>, a DomainAction<T> or a VoidDomainAction. No invoker is generated: if the work is in Execute the attribute does nothing and should go, and if you followed the convention of leaving Execute empty the action does nothing at all.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // A composite reachable by anyone (PRAG0440)
    // =========================================================================

    // A [CompositeAction] with an [Endpoint] is the door to its steps, and the door has to say who may
    // come through it. By default each step keeps its own permission check (the strict rule nested
    // children already had); only a composite that declares [AbsorbsChildPermissions] runs its steps
    // as internal calls, and then nothing but the composite's own declaration stands between the
    // caller and the writes.
    //
    // What this prevents, for a composite that absorbs: an authenticated caller holding NO
    // permissions succeeds and every step writes, where each step's own endpoint would answer 403.
    // The generated endpoint of a composite that declares nothing carries no authorization line;
    // RequireAuthorizationByDefault demands only that the caller be authenticated, which is why the
    // request succeeds instead of answering 401.
    //
    // Error, not warning: with [AbsorbsChildPermissions] this is a fail-open, and the shape it takes —
    // a permissioned mutation wrapped in a composite that forgot to say so — is a privilege escalation
    // by composition. [AllowAnonymous] is the way to say a composite is deliberately public, and it
    // satisfies this. The message does not claim the steps' checks are suppressed by construction:
    // absorbing is a declaration, [AbsorbsChildPermissions].
    public static readonly DiagnosticDescriptor CompositeExposedWithoutPermission = new(
        "PRAG0440",
        "Exposed composite action declares no permission",
        "'{0}' is a [CompositeAction] with an [Endpoint] and declares no permission of its own, but its "
        + "step{1} {2} require{3} one. The composite is the door to this route and the door says "
        + "nothing: an authenticated caller holding nothing at all reaches the steps, and their own "
        + "checks are all that stands between the caller and the writes — nothing at all once the "
        + "composite declares [AbsorbsChildPermissions]. Declare [RequirePermission] or "
        + "[RequirePolicy<T>] on '{0}', or [AllowAnonymous] if it is meant to be public.",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // An event parameter with no source (PRAG0433)
    // =========================================================================

    // [Raises<TEvent>] binds the event's constructor parameters by name to the operation's input
    // properties, and for a mutation to the entity's as well. A parameter that matches nothing is
    // emitted as `default` — Guid.Empty, or null — and the event is dispatched anyway. A handler then
    // reads a blank id and nothing failed anywhere: the write happened, the event arrived, and only its
    // contents are wrong.
    //
    // The same shape is already reported for routes (PRAG0504, a {placeholder} with no property), and
    // this is the reason: a name that does not match is nearly always a typo, and the alternative to
    // saying so is a value that looks deliberate.
    public static readonly DiagnosticDescriptor EventParameterHasNoSource = new(
        "PRAG0433",
        "An event parameter has nothing to bind to",
        "'{0}' raises {1}, whose parameter '{2}' matches no input property{3} — it is passed as default, so the handler receives an empty value. Rename the parameter to match, or add the property.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // A composite whose boundary nothing names (PRAG0447)
    // =========================================================================

    // The generated CompositeInvoker takes an IUnitOfWork, and that service is registered KEYED by
    // boundary — so the constructor needs [FromKeyedServices(typeof(TBoundary))], and to write it the
    // generator has to know which boundary. A mutation gets it from its entity; a composite has no
    // entity, only steps.
    //
    // Without it the composite would ask for an unkeyed IUnitOfWork nobody registers, and the
    // application would not start: a container validation error naming a generated type, mentioning
    // neither the composite nor the attribute that would fix it. The namespace answers for almost every composite
    // — the same match every other member of a boundary already uses — and this says so for the ones
    // it does not.
    //
    // Warning, not error: an application that registers an unkeyed IUnitOfWork of its own works,
    // and turning its build red would be this diagnostic causing the breakage it exists to prevent.
    public static readonly DiagnosticDescriptor CompositeWithoutBoundary = new(
        "PRAG0447",
        "The composite belongs to no boundary",
        "'{0}' is a [CompositeAction] and no boundary claims it: its namespace matches none, and it declares no [BelongsTo<TBoundary>]. The generated invoker needs a unit of work, which is registered per boundary, so the application will fail to start. Name the boundary with [BelongsTo<TBoundary>], or move the type under the boundary's namespace.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // A boundary-keyed service declared where no boundary answers (PRAG0448)
    // =========================================================================

    /// <summary>
    ///     PRAG0448 — the operation declares a <c>DbContext</c> or an <c>IUnitOfWork</c>, and no boundary
    ///     claims it, so the generated invoker cannot write the key those two are registered under.
    /// </summary>
    /// <remarks>
    ///     The same shape as <see cref="CompositeWithoutBoundary" /> one level down, and the same
    ///     symptom: an unkeyed resolution nobody registers, a container validation error at startup
    ///     naming a generated type, and neither the field nor the fix mentioned anywhere. A warning
    ///     rather than an error because the operation may still be legitimate — nothing else about it
    ///     needs a boundary — but that field will not resolve.
    ///     <para>
    ///         ⚠️ Only where the assembly declares boundaries and none of them claims this operation.
    ///         A module that declares none is a library whose host supplies the context — six operations
    ///         in <c>Pragmatic.Authorization.Management</c> are exactly that shape — and telling them
    ///         they will fail to start would be a guess dressed as a fact.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor KeyedServiceWithoutBoundary = new(
        "PRAG0448",
        "The keyed service belongs to no boundary",
        "'{0}' declares '{1}', which is registered per boundary, and no boundary claims this operation: "
        + "its namespace matches none and it declares no [BelongsTo<TBoundary>]. The generated invoker "
        + "will ask for it without a key and the application will fail to start. Name the boundary with "
        + "[BelongsTo<TBoundary>], or move the type under the boundary's namespace.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // A commit declaration with no unit of work to govern (PRAG0432)
    // =========================================================================

    // Both [Transactional] and [CommitStrategy] act on the invoker's unit of work, and the invoker only
    // has one when the action belongs to a boundary. The boundary is normally inferred from the entity
    // behind an IRepository field — which an action that only composes does not have. So the attribute
    // parses, generates nothing, and the author is left with a declaration that is inert.
    //
    // Error, not warning. The two other outcomes of this shape are already errors, and the alternative
    // here is worse than a missing feature: [Transactional] on an action that composes cross-boundary
    // work reads as atomicity, and silently provides none.
    public static readonly DiagnosticDescriptor CommitDeclarationWithoutBoundary = new(
        "PRAG0432",
        "The commit declaration has no unit of work to govern",
        "'{0}' declares [{1}] but belongs to no boundary, so there is no unit of work and the attribute does nothing. A boundary is inferred from the entity behind an IRepository field, which an action that only composes does not have — name it with [BelongsTo<TBoundary>].",
        Category, DiagnosticSeverity.Error, true);

    // =========================================================================
    // [Transactional] on a boundary (PRAG0431)
    // =========================================================================

    // A boundary can carry [CommitStrategy] because "how work here commits" is a policy, and one policy
    // covers every action of the boundary at no cost. [Transactional] is not a policy: it opens a
    // database transaction per invocation, so as a boundary-wide default it would buy a round trip for
    // every action including the ones that write nothing. It is read per action, and on a boundary it
    // does nothing — which, unsaid, is the shape this codebase keeps finding.
    public static readonly DiagnosticDescriptor TransactionalOnBoundary = new(
        "PRAG0431",
        "A boundary cannot declare a transaction",
        "Boundary '{0}' declares [Transactional]. It has no effect: a transaction is opened per invocation, and as a boundary-wide default it would cost a round trip on every action. Declare [Transactional] on the actions that need it, or [CommitStrategy] here if what you want is a boundary-wide commit policy.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // PerStep on a composite (PRAG0430)
    // =========================================================================

    // A composite exists to make several steps one unit — that is the whole of what [CompositeAction]
    // says. PerStep asks for the opposite, and the invoker cannot honour both: it commits once, always.
    // Unreported, the attribute would be read by nobody and the composite would go on committing once:
    // a declaration that reads as a choice and does nothing.
    public static readonly DiagnosticDescriptor PerStepOnComposite = new(
        "PRAG0430",
        "A composite cannot commit per step",
        "'{0}' is a [CompositeAction] and declares [CommitStrategy(CommitMode.PerStep)]. A composite commits once by construction and the attribute has no effect. Remove it, or drop [CompositeAction] and compose in the body, where PerStep is honoured.",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // Compensation across a boundary is a saga without a log (PRAG0429)
    // =========================================================================

    // The answer PRAG0424 accepts, described honestly. [UndoWith<T>] does what a saga's compensating
    // step does — and none of what makes a saga a saga: the owed undo is not written down anywhere, so
    // it does not survive the process, is never retried, and leaves no trace that it was owed. The
    // window is the one between the inner boundary's commit and the undo running, and a crash inside it
    // keeps the write forever.
    //
    // Info, not warning. The decision was already made and recorded — warning again would be nagging,
    // and under --warnaserror it would punish the applications that chose correctly. What this adds is
    // that the reader of the call knows what they are looking at without reading the framework.
    public static readonly DiagnosticDescriptor CompensationIsNotDurable = new(
        "PRAG0429",
        "Compensation across a boundary is a saga without a log",
        "'{0}' undoes {1} in-request. That is a saga's compensating step without a saga's durability: the undo is not recorded, so a crash between the other boundary's commit and the undo keeps the write, with nothing to retry it and no trace that it was owed. Where that matters, publish an event or model the flow as a saga.",
        Category, DiagnosticSeverity.Info, true);

    // =========================================================================
    // Composition without a declared commit strategy (PRAG0428)
    // =========================================================================

    // Composing small actions is the point — it is how a domain is built without rewriting code — and
    // it is also where the commit stops being obvious. Three outcomes, all reasonable, none of them
    // implied by the code: atomic with the steps visible to each other, one commit without that
    // visibility, or steps that stand alone. An action that says nothing takes one of them in silence,
    // and its behaviour then depends on a default declared in another repository.
    //
    // Warning, and only where composition exists: an action that invokes nothing is one transaction
    // whatever the default, and has nothing to declare.
    public static readonly DiagnosticDescriptor UndeclaredCompositionStrategy = new(
        "PRAG0428",
        "Composition does not say how its steps commit",
        "'{0}' invokes other actions or mutations without saying how they commit. Declare [Transactional] (one transaction, and each step sees what the previous wrote), [CommitStrategy(CommitMode.Once)] (a single commit, no intermediate visibility) or [CommitStrategy(CommitMode.PerStep)] (independent steps, a failure keeps what came before).",
        Category, DiagnosticSeverity.Warning, true);

    // =========================================================================
    // A package operation needs a boundary the importer did not name (PRAG0449/PRAG0450)
    // =========================================================================

    /// <summary>
    ///     PRAG0449 — an imported package operation asks for a boundary-keyed service, and the module
    ///     that imported it named no boundary.
    /// </summary>
    /// <remarks>
    ///     The other half of <see cref="KeyedServiceWithoutBoundary" />. A package declares no boundary
    ///     of its own — that is what makes it a package — so its invoker asks for <c>DbContext</c>
    ///     unkeyed and its constructor is fixed in the package's compilation. The importer is the only
    ///     place a key can come from, so it has to say: <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c>.
    ///     An error, because the alternative is an application that starts and throws from the first
    ///     request that reaches the operation.
    /// </remarks>
    public static readonly DiagnosticDescriptor PackageNeedsABoundaryAtTheImport = new(
        "PRAG0449",
        "The imported package needs a boundary",
        "The imported operation '{0}' needs {1}, which is registered keyed by boundary, and this module imported the package without naming one. Import it as [UsePackage<TPackage, {2}>] so the package's operations use this module's boundary.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     PRAG0450 — two imports on the same module name different boundaries.
    /// </summary>
    /// <remarks>
    ///     The bridge the importer emits is a single unkeyed registration in the container, so a second
    ///     boundary would shadow the first and one package's operations would silently read the other's
    ///     database. One module, one answer.
    /// </remarks>
    public static readonly DiagnosticDescriptor PackageImportsNameDifferentBoundaries = new(
        "PRAG0450",
        "Two package imports name different boundaries",
        "This module imports packages naming more than one boundary ({0}). The unkeyed registration that answers a package's DbContext is one per container, so the second would shadow the first: name the same boundary on every import.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     PRAG0463: a method carries <c>[Invariant]</c> and the generated invoker cannot call it, so the
    ///     rule is enforced on no path at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>An error, not a warning, and that is the whole point.</b> A rule that cannot fire is
    ///         not a degraded check — it is the absence of one, written in the entity's source as a
    ///         guarantee nothing keeps. Without it an <c>[Invariant]</c> written <c>private</c> is
    ///         dropped in silence and the operation it is meant to refuse answers <b>201 Created</b>;
    ///         the method name appears in <b>no</b> generated file. An
    ///         author who wants the method private wants it not to be an invariant.
    ///     </para>
    ///     <para>
    ///         The message names <b>which</b> of the conditions the method fails, because they are five
    ///         and a message saying only "cannot be called" sends the author to read the accessibility
    ///         of a method whose real problem is its return type. The attribute is read before the shape
    ///         filter runs: the other way round, a method that asks to be a rule is indistinguishable
    ///         from one that never did.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor InvariantCannotBeCalled = new(
        "PRAG0463",
        "The invariant cannot be called, so the rule never fires",
        "'{0}' carries [Invariant] but the generated invoker cannot call it: {1}. The rule is enforced on no path — make the method a parameterless instance method returning bool, at least internal, with a name no other invariant of this entity uses, or remove the attribute.",
        Category, DiagnosticSeverity.Error, true);

    /// <summary>
    ///     PRAG0464: <c>[Retry]</c>, <c>[Timeout]</c> or <c>[CircuitBreaker]</c> on a class no engine
    ///     reads it on.
    /// </summary>
    /// <remarks>
    ///     Two engines read them: jobs (<c>[Retry]</c> and <c>[Timeout]</c> on a <c>[Job]</c> or
    ///     <c>[RecurringJob]</c>) and message handlers (all three on a <c>[MessageHandler]</c>). On a
    ///     <c>[DomainAction]</c> or a <c>[Mutation]</c> the attribute compiled, counted as a resilience
    ///     declaration, and the operation ran once — what an action takes is <c>[ResiliencePolicy]</c>.
    ///     A Warning: the class works, only without the behaviour the attribute promises.
    /// </remarks>
    public static readonly DiagnosticDescriptor ResilienceAttributeNothingReads = new(
        "PRAG0464",
        "Nothing reads this resilience attribute on this kind of class",
        "[{0}] on '{1}' is read by nothing: {2}. On a domain action, declare [ResiliencePolicy(\"name\")] and define the policy.",
        Category, DiagnosticSeverity.Warning, true);
}
