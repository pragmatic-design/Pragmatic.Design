using System.Collections.Immutable;
using Pragmatic.SourceGen;

using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a Query class definition.
///     Used for incremental generator caching.
/// </summary>
internal sealed record QueryModel
{
    /// <summary>
    ///     The namespace of the query class.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The name of the query class.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The full type name including namespace.
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The type keyword (class, record).
    /// </summary>
    public required string TypeKind { get; init; }

    /// <summary>
    ///     Whether this is a record type.
    /// </summary>
    public bool IsRecord { get; init; }

    /// <summary>
    ///     The fully qualified name of the entity type (TEntity).
    /// </summary>
    public required string EntityTypeFullName { get; init; }

    /// <summary>
    ///     The simple name of the entity type.
    /// </summary>
    public required string EntityTypeName { get; init; }

    /// <summary>
    ///     The fully qualified name of the result type (TResult).
    /// </summary>
    public required string ResultTypeFullName { get; init; }

    /// <summary>
    ///     The simple name of the result type.
    /// </summary>
    public required string ResultTypeName { get; init; }

    /// <summary>
    ///     What the query answers, fully qualified: the result for a <c>Single</c> query, a
    ///     <c>PagedResult</c> of it for a paged one, a read-only list of it otherwise.
    /// </summary>
    /// <remarks>
    ///     One rule for the query's invoker, which returns it, and for an operation's <c>[LoadFrom]</c>
    ///     property, which must be of it.
    /// </remarks>
    public string AnswerTypeFullName => IsSingle
        ? $"global::{ResultTypeFullName}"
        : IsPaged
            ? $"global::Pragmatic.Persistence.Query.Results.PagedResult<global::{ResultTypeFullName}>"
            : $"global::System.Collections.Generic.IReadOnlyList<global::{ResultTypeFullName}>";

    /// <summary>
    ///     Whether entity and result types are the same.
    /// </summary>
    public bool IsSameEntityAndResult => EntityTypeFullName == ResultTypeFullName;

    /// <summary>
    ///     Navigation paths declared with <c>[EagerLoad]</c> on the query.
    /// </summary>
    public EquatableArray<string> EagerLoadPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The <c>[EagerLoad]</c> paths that name no navigation — reported (PRAG0736), not in <see cref="EagerLoadPaths" />.</summary>
    public EquatableArray<EagerLoadProblemModel> EagerLoadProblems { get; init; } = EquatableArray<EagerLoadProblemModel>.Empty;

    /// <summary>
    ///     The DTO the operation answers with, when it declares one that maps from an entity.
    /// </summary>
    /// <remarks>
    ///     Its <c>RequiredNavigations</c> is the set the DTO reaches through, already worked out by
    ///     Mapping. Naming it is what lets the query load them instead of deriving a second, poorer
    ///     version of the same rule.
    /// </remarks>
    public string? ResponseDtoFullTypeName { get; init; }

    /// <summary>
    ///     Whether the query materialises the entity and therefore needs its navigations loaded.
    /// </summary>
    /// <remarks>
    ///     A query answering with a DTO projects — <c>filtered.Select(query.Projection)</c> — and EF
    ///     turns a flattened path into a JOIN, so an include there would add nothing. It is the
    ///     entity-shaped result that comes back with its navigations empty.
    /// </remarks>
    /// <remarks>
    ///     ⚠️ <b>And the one answering with a DTO it maps in memory.</b> <c>IsSameEntityAndResult</c>
    ///     alone reads as "the result is the entity", while the gate means "the query does not
    ///     project" — the same thing for every shape but one. <c>MapInMemory</c> is precisely the
    ///     query that answers with a DTO and does <b>not</b> project: the executor materialises the
    ///     rows and runs <c>MapEach</c> over them, reading the entity's navigations in the process.
    ///     Left out of the gate, every path such a query declares would be dropped in silence and the
    ///     mapped property would come back null.
    /// </remarks>
    /// <remarks>
    ///     ⚠️ A resolved <c>[Join&lt;T&gt;(Via = …)]</c> counts, and that is the whole of what the
    ///     attribute does: its path is an include path like any other. The
    ///     <c>IsSameEntityAndResult</c> gate above still decides, and on a projecting query a join
    ///     produces nothing — correctly: an <c>Include</c> emitted inside <c>Apply</c> would be
    ///     dropped by EF Core there anyway.
    /// </remarks>
    public bool NeedsEagerLoading =>
        (IsSameEntityAndResult || MapsInMemory)
        && (EagerLoadPaths.Length > 0
            || ResponseDtoFullTypeName is not null
            || LoadingProfileFullTypeName is not null
            || Joins.Any(j => j.IsResolvedNavigationJoin));

    /// <summary>
    ///     The generated loading profile this query's own <c>[LoadWith&lt;T&gt;]</c> produces, or null.
    /// </summary>
    /// <remarks>
    ///     <c>[LoadWith]</c> generated a profile that nothing called: <c>ApplyIncludes()</c> is a typed
    ///     extension, and the read path applies <c>Include(string)</c> through
    ///     <c>IIncludableQuery.IncludePaths</c>. Naming the profile's string list here is what turns the
    ///     attribute into an eager load that happens.
    /// </remarks>
    public string? LoadingProfileFullTypeName { get; init; }

    /// <summary>
    ///     All properties in this query class.
    /// </summary>
    public EquatableArray<QueryPropertyModel> Properties { get; init; } = EquatableArray<QueryPropertyModel>.Empty;

    /// <summary>
    ///     All joins declared on this query class.
    /// </summary>
    public EquatableArray<JoinModel> Joins { get; init; } = EquatableArray<JoinModel>.Empty;

    /// <summary>
    ///     Where the query class is declared, for the diagnostics that name it. Excluded from equality
    ///     by <see cref="LocationInfo" />, so it does not disturb the incremental cache.
    /// </summary>
    public LocationInfo? Location { get; init; }

    /// <summary>
    ///     How many <c>[PreProcessor&lt;T&gt;]</c> / <c>[PostProcessor&lt;T&gt;]</c> the query declares —
    ///     all of them inert.
    /// </summary>
    /// <remarks>
    ///     The processors are rendered by the handler templates for <c>Endpoint&lt;T&gt;</c> classes,
    ///     mutations and domain actions. The query handler template does not read them, so on a query
    ///     they register nothing and run never. Counted so PRAG0703 can say so.
    /// </remarks>
    public int InertProcessorCount { get; init; }

    /// <summary>
    ///     Whether the result type has the <c>Projection</c> the generated <c>Apply</c> names.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>True unless something looked and did not find it.</b> Only <c>QueryTransform</c> can
    ///         answer the question — it holds the DTO's symbol and reads <c>[GenerateProjection]</c> off
    ///         it. Every other producer of a <c>QueryModel</c> is a feature generating the DTO in this
    ///         same compilation, where the attribute cannot be read because the type does not exist yet.
    ///     </para>
    ///     <para>
    ///         Defaulting to <c>false</c> would put that backwards: <c>[Resource]</c>, and the comment,
    ///         tag, note and attachment traits would all report PRAG0704 against DTOs they write
    ///         themselves, with a projection, on the line below. A default of <c>true</c> means a
    ///         producer that forgets the flag is silent rather than wrong.
    ///     </para>
    /// </remarks>
    public bool ResultTypeGeneratesAProjection { get; init; } = true;

    /// <summary>
    ///     Filter properties (required or marked with [Filter]).
    /// </summary>
    public IEnumerable<QueryPropertyModel> FilterProperties => Properties.Where(p => p.IsFilter);

    /// <summary>
    ///     Sort properties (marked with [Sort]).
    /// </summary>
    public IEnumerable<QueryPropertyModel> SortProperties => Properties
        .Where(p => p.IsSort)
        .OrderBy(p => p.SortPriority);

    /// <summary>
    ///     Whether this query has paging properties (Page and PageSize).
    /// </summary>
    public bool HasPaging => Properties.Any(p => p.IsPageProperty) && Properties.Any(p => p.IsPageSizeProperty);

    /// <summary>
    ///     Whether the generated half writes <c>Page</c> and <c>PageSize</c> into the query.
    /// </summary>
    /// <remarks>
    ///     Set only when the author asked for them with <c>Paged = true</c> and did not write them:
    ///     two declarations of <c>Page</c> in one partial class is <c>CS0102</c>, in the half the
    ///     author cannot edit. The redundant ask is <c>PRAG0727</c>, not a second copy.
    /// </remarks>
    public bool GeneratesPaging { get; init; }

    /// <summary>
    ///     Whether <c>Paged = true</c> was asked for on a query that already pages by hand.
    /// </summary>
    public bool RedundantPagingRequest { get; init; }

    /// <summary>
    ///     The Page property (if exists).
    /// </summary>
    public QueryPropertyModel? PageProperty => Properties.FirstOrDefault(p => p.IsPageProperty);

    /// <summary>
    ///     The PageSize property (if exists).
    /// </summary>
    public QueryPropertyModel? PageSizeProperty => Properties.FirstOrDefault(p => p.IsPageSizeProperty);

    /// <summary>
    ///     Whether this query has any sort properties.
    /// </summary>
    public bool HasSorting => Properties.Any(p => p.IsSort);

    /// <summary>
    ///     Whether this query has any filter properties.
    /// </summary>
    public bool HasFilters => Properties.Any(p => p.IsFilter);

    /// <summary>
    ///     Whether this query has any joins declared.
    /// </summary>
    public bool HasJoins => Joins.Length > 0;

    /// <summary>
    ///     The key joins this query generates: the target is reached by key, every name resolves, and
    ///     the join type is one EF Core can translate.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Order is the declaration order and it is load-bearing: the generated step chains them in
    ///     it, and <see cref="JoinedResultPropertyModel.JoinIndex" /> is an index into this list.
    /// </remarks>
    public EquatableArray<JoinModel> KeyJoins =>
        Joins.Where(j => j.IsResolvedKeyJoin && j.IsGeneratableJoinType).ToImmutableArray();

    /// <summary>
    ///     Whether the join owns the whole step — filtered set in, projected set out.
    /// </summary>
    /// <remarks>
    ///     A key join carries columns no navigation reaches, and those cannot travel in a
    ///     <c>Projection</c>, which is one entity in and one result out. So a query with one generates
    ///     <c>Aggregate</c> instead, and the two are exclusive by the executor's own contract.
    ///     <para>
    ///         ⚠️ Not when the result <b>is</b> the entity: there is nothing to project into, so the
    ///         join would have no column to deliver. That is <c>PRAG0738</c>.
    ///     </para>
    ///     <para>
    ///         ⚠️ And not without a boundary. The target's set is read from the root boundary's own
    ///         <c>DbContext</c> — EF Core composes a join only inside one instance — so an entity that
    ///         names no boundary has no context to read it from. Reported as <c>PRAG0703</c>, which is
    ///         what "you declared it and it did not take effect" is for.
    ///     </para>
    /// </remarks>
    public bool GeneratesJoinedProjection =>
        !IsSameEntityAndResult && KeyJoins.Length > 0 && BoundaryTypeName is not null;

    /// <summary>
    ///     Whether a declared key join was refused, so the query generates no result member at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Silence here is the point. A refused key join takes <see cref="GeneratesJoinedProjection" />
    ///     with it, and without this the query would fall back to naming
    ///     <c>{TResult}.Projection</c> — a member a joined result type never declares. The author would
    ///     then read three things for one mistake: the diagnostic that explains it, a <c>PRAG0704</c>
    ///     about a projection they never wanted, and a <c>CS0117</c> inside a generated file. Measured,
    ///     on the Showcase's own declaration.
    /// </remarks>
    public bool HasRefusedKeyJoin =>
        Joins.Any(j => j.IsKeyJoin && (!j.TargetIsReachable || !j.IsGeneratableJoinType));

    /// <summary>
    ///     Where each settable property of the result type reads its value, for a joined projection.
    /// </summary>
    public EquatableArray<JoinedResultPropertyModel> JoinedResultProperties { get; init; } =
        EquatableArray<JoinedResultPropertyModel>.Empty;

    /// <summary>
    ///     Result properties that neither the entity nor any joined target can answer.
    /// </summary>
    public EquatableArray<string> UnresolvedResultProperties { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Complex filter properties marked with [ComplexFilter].
    ///     Each entry generates a call to <c>{Type}Extensions.ApplyFilter(query, this.{Property})</c>.
    /// </summary>
    public EquatableArray<QueryComplexFilterModel> ComplexFilters { get; init; } =
        EquatableArray<QueryComplexFilterModel>.Empty;

    /// <summary>Whether this query has any complex filter properties.</summary>
    public bool HasComplexFilters => ComplexFilters.Length > 0;

    /// <summary>
    ///     Properties whose type is a <c>Specification&lt;TEntity&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Each entry generates a <c>query.Where(this.{Property})</c> in <c>Apply()</c> and a
    ///     conjunction in <c>ToSpecification()</c>. A rule written once as a specification is reused by
    ///     the query instead of being restated as a filter — which is where the duplication came from.
    /// </remarks>
    public EquatableArray<QuerySpecificationModel> Specifications { get; init; } =
        EquatableArray<QuerySpecificationModel>.Empty;

    /// <summary>
    ///     The class was declared <c>partial</c>, so the generator may add members to it.
    /// </summary>
    /// <remarks>
    ///     Carried rather than answered as a dropped model: a <c>null</c> from the transform is
    ///     indistinguishable from "not my node", and <c>[Query]</c> on a class somebody forgot to make
    ///     partial would generate no Apply, no projection and no endpoint — in silence.
    ///     PRAG0712, the companion analyzer's, says it on the declaration.
    /// </remarks>
    public bool IsPartial { get; init; } = true;

    /// <summary>Whether this query composes any specification.</summary>
    public bool HasSpecifications => Specifications.Length > 0;

    /// <summary>
    ///     The <c>[FromCurrentUser]</c> properties, which the generated invoker fills from the caller
    ///     before the read.
    /// </summary>
    public EquatableArray<CurrentUserBindingModel> CurrentUserBindings { get; init; } =
        EquatableArray<CurrentUserBindingModel>.Empty;

    /// <summary>
    ///     The <c>[FromClock]</c> properties, which the generated invoker fills from the application's
    ///     clock before the read.
    /// </summary>
    public EquatableArray<ClockBindingModel> ClockBindings { get; init; } =
        EquatableArray<ClockBindingModel>.Empty;

    /// <summary>
    ///     Inputs whose type is the canonical <c>GridFilterRequest</c>: what a data grid asks for, taken
    ///     as data.
    /// </summary>
    /// <remarks>
    ///     Each one generates a call to the entity's generated bridge inside <c>Apply()</c>. Before this,
    ///     the bridge existed only as an extension over the queryable, so a grid read had to be a
    ///     <c>[DomainAction]</c> that fetched <c>Query()</c> and applied the request itself — and an
    ///     action that reads through a repository declares no read, so it appears in no processing
    ///     register and publishes no contract for the fields the grid may name.
    /// </remarks>
    public EquatableArray<QueryGridRequestModel> GridRequests { get; init; } =
        EquatableArray<QueryGridRequestModel>.Empty;

    /// <summary>Whether the query takes a canonical grid request.</summary>
    public bool HasGridRequests => GridRequests.Length > 0;

    /// <summary>
    ///     Whether the entity carries <c>[GenerateGridBridge]</c>, and therefore has the bridge the
    ///     generated <c>Apply</c> names.
    /// </summary>
    /// <remarks>
    ///     The attribute is the bridge's only trigger, so reading it off the entity answers the question
    ///     completely — including for an entity from a referenced assembly, whose bridge was generated in
    ///     that compilation. PRAG0723 reports the query that asks for a bridge nobody declared, instead of
    ///     letting a CS0103 land in a generated file the author cannot edit.
    /// </remarks>
    public bool EntityDeclaresGridBridge { get; init; }

    /// <summary>
    ///     The generated bridge's full type name, <c>{entity namespace}.{Entity}GridFilterBridge</c>.
    /// </summary>
    /// <remarks>
    ///     Derived from the entity's own name rather than carried, because the bridge template derives it
    ///     the same way from the same symbol — two copies of one naming rule is how they drift apart.
    /// </remarks>
    public string GridBridgeTypeName
    {
        get
        {
            var separator = EntityTypeFullName.LastIndexOf('.');
            var entityNamespace = separator < 0 ? "" : EntityTypeFullName.Substring(0, separator + 1);
            return $"{entityNamespace}{EntityTypeName}GridFilterBridge";
        }
    }

    /// <summary>
    ///     Inputs marked <c>[BindSpecification]</c>: read by a specification rather than by a generated
    ///     filter.
    /// </summary>
    /// <remarks>
    ///     Carried so the feature can report PRAG0709 — the attribute claims a consumer, and a query
    ///     with no specification at all has none.
    /// </remarks>
    public EquatableArray<string> SpecificationInputs { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether the query class inherits from another query.
    /// </summary>
    /// <summary>
    ///     How the source is narrowed before the executor sees it: tracking, and whether EF's own query
    ///     filters are lifted.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Carried on the model because the invoker does the read, so the invoker has to know it: an
    ///     invoker that resolved a plain <c>Set</c> would silently re-apply the
    ///     tenant and soft-delete filters a <c>Raw</c> query exists to lift, and start tracking rows a
    ///     projection said would never be written.
    /// </remarks>
    public QueryStrategyKind? Strategy { get; init; }

    /// <summary>Whether the query overrides a named filter, which lifts EF's filters for the read.</summary>
    public bool HasFilterOverrides { get; init; }

    /// <summary>The boundary whose keyed context the generated invoker reads from.</summary>
    /// <remarks>
    ///     Null when the entity names no boundary, and then the invoker resolves the unkeyed
    ///     <c>DbContext</c> — the same fallback the generated endpoint has always had.
    /// </remarks>
    public string? BoundaryTypeName { get; init; }

    /// <summary>Whether the query pages, which decides which executor overload the invoker calls.</summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="HasPaging" /> and nothing else: that is what decides whether the query
    ///         implements <c>IPagedQuery</c>, and the executor overload the invoker calls has to be the
    ///         one that interface satisfies. Deriving it a second way — even from the same two
    ///         properties — is how an invoker comes to call an overload the query cannot be passed to.
    ///     </para>
    ///     <para>
    ///         ⚠️ Computed, not an <c>init</c>: the transform would fill one in and the programmatic
    ///         models would not, so a scaffolded list would answer through an invoker of the list shape
    ///         while its handler unwraps a page, and the generated file would not compile.
    ///     </para>
    /// </remarks>
    public bool IsPaged => HasPaging;

    /// <summary>Whether the query answers one row.</summary>
    public bool IsSingle { get; init; }

    /// <summary>Whether the result type is mapped after the rows arrive rather than projected.</summary>
    /// <remarks>
    ///     ⚠️ Declared on the attribute, never inferred from the result type. A DTO without
    ///     <c>[GenerateProjection]</c> reads as an omission, and turning that into a silent
    ///     materialisation is the trap the projection exists to avoid.
    /// </remarks>
    /// <summary>
    ///     True when the result type declares its own aggregation — a <c>[QueryView&lt;TEntity&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Such a result cannot be reached by a row projection: <c>Projection</c> is one entity in
    ///     and one result out, and a <c>GROUP BY</c> answers with groups. The view already generates
    ///     the whole step as <c>Build(IQueryable&lt;TEntity&gt;)</c>, so the query names that instead of
    ///     restating the grouping — and instead of naming a <c>Projection</c> the view does not have,
    ///     which is a CS0117 inside a file the author cannot edit.
    /// </remarks>
    /// <summary>
    ///     Explicit group for the boundary interface, when the namespace cannot say it.
    /// </summary>
    /// <remarks>
    ///     Null for a query the author wrote: its group comes from its namespace, in the same pass that
    ///     infers an action's. ⚠️ A <b>scaffolded</b> query takes the entity's namespace, which is flat
    ///     by rule, so it has no segment to infer from and landed on the boundary root while its
    ///     hand-written neighbours were grouped.
    /// </remarks>
    public string? SubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Name = …)]</c> says, verbatim, or null when it is not written.</summary>
    public string? DeclaredSubBoundaryName { get; init; }

    /// <summary>What <c>[SubBoundary(Description = …)]</c> says: the group interface's summary.</summary>
    public string? SubBoundaryDescription { get; init; }

    public bool ResultIsAggregateView { get; init; }

    public bool MapsInMemory { get; init; }

    /// <summary>
    ///     The permissions the query declares, as literals the invoker enforces.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Resolved against the permission catalog before they reach here, because a permission is
    ///     often a constant this same generator writes and no compilation can bind it while it is being
    ///     written. A path left unresolved is dropped rather than emitted as source, which would not
    ///     compile in a file that has none of the author's <c>using</c> directives.
    /// </remarks>
    public EquatableArray<string> RequiredPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The unresolved constant paths, kept so the feature can report what it could not bind.</summary>
    public EquatableArray<string> UnresolvedPermissionPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Whether the query opts out of the auto-derivation posture with <c>[AllowAnonymous]</c>.</summary>
    /// <remarks>
    ///     ⚠️ Here because the derived name has to reach the <b>invoker</b>, not only the route: the
    ///     boundary facade calls the invoker in process, so a name written on one door alone leaves the
    ///     other open. Both doors ask <c>ActionsFeature.ResolvePermission</c>, and it can only agree
    ///     with itself if it is asked the same question — which means the query model has to know the
    ///     same two things the endpoint model knows.
    /// </remarks>
    public bool AllowAnonymous { get; init; }

    /// <summary>The name <c>[ExplicitPermission]</c> supplies instead of a derived one.</summary>
    public Actions.Models.ExplicitPermissionModel? ExplicitPermission { get; init; }

    /// <summary>Whether every declared permission is needed, or any one of them.</summary>
    public bool RequiresAllPermissions { get; init; } = true;

    public bool HasBaseQuery { get; init; }

    /// <summary>
    ///     The base query type name (if inherits).
    /// </summary>
    public string? BaseQueryTypeName { get; init; }

    /// <summary>
    ///     Interface to implement (IQuery or IPagedQuery).
    /// </summary>
    public string InterfaceToImplement => HasPaging
        ? $"IPagedQuery<global::{EntityTypeFullName}, global::{ResultTypeFullName}>"
        : $"IQuery<global::{EntityTypeFullName}, global::{ResultTypeFullName}>";

    /// <summary>
    ///     Whether model is valid for code generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(TypeName) &&
                           !string.IsNullOrEmpty(EntityTypeFullName) &&
                           !string.IsNullOrEmpty(ResultTypeFullName) &&
                           UnresolvedResultType is null;

    /// <summary>
    ///     The name of the projected type when it does not resolve — as the author wrote it, because
    ///     that is all an error symbol has.
    /// </summary>
    /// <remarks>
    ///     An error symbol is still an <c>INamedTypeSymbol</c>, so every check here passes and its
    ///     fully-qualified display is the bare name with no namespace. Written back out it becomes
    ///     <c>global::CustomerDto</c> in the query, its invoker and all three boundary facades — a page
    ///     of <c>CS0400</c> in files the author cannot edit, from one missing <c>using</c> in one file
    ///     they can. Nothing is generated for the query; <c>PRAG9001</c> names it.
    /// </remarks>
    public string? UnresolvedResultType { get; init; }
}
