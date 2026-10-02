using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Query Pipeline source generation.
/// </summary>
/// <remarks>
///     <para>
///         The module's range is <c>PRAG0700-PRAG0799</c>. This class defines <c>PRAG0701-PRAG0705</c>,
///         <c>PRAG0707-PRAG0716</c>, <c>PRAG0722-PRAG0729</c> and <c>PRAG0736-PRAG0741</c>; the other families in the range live where the code that
///         emits them does — <c>PRAG0706</c> in <see cref="ReadAccessDiagnostics" />,
///         <c>PRAG0717-PRAG0721</c> in <c>VisibilityRuleDescriptors</c>, <c>PRAG0730-PRAG0731</c> in
///         <see cref="CurrentUserBindingDiagnostics" /> and <c>PRAG0732</c> in <see cref="QueryViewDiagnostics" />.
///     </para>
///     <para>
///         ⚠️ <c>PRAG0700</c> is not defined, here or anywhere. A range is where a module's ids come
///         from, and holes in it are ordinary — <c>PRAG0601</c> and <c>PRAG0603-0609</c> are holes in the
///         Persistence range for the same reason. A case that needs an id takes the next free one; the
///         free ids are listed in <c>docs/diagnostics.md</c>.
///     </para>
/// </remarks>
internal static class QueryPipelineDiagnostics
{
    // =========================================================================
    // Warnings (PRAG0701-0709)
    // =========================================================================

    /// <summary>
    ///     A required reference navigation whose target carries the soft-delete query filter. EF Core
    ///     translates a required navigation into an INNER JOIN, so the target's <c>!IsDeleted</c> filter
    ///     removes the DEPENDENT rows as well: they stay in the database but vanish from every query that
    ///     joins the navigation.
    ///     <para>
    ///     Reported by <see cref="Validation.SoftDeleteNavigationValidator"/>, which deliberately stays
    ///     silent on the two shapes where the disappearance is the declared intent or cannot happen at all
    ///     — see that class for the calibration.
    ///     </para>
    /// </summary>
    public static readonly DiagnosticDescriptor SoftDeleteRequiredNavigation = DiagnosticFactory.Warning(
        "PRAG0705",
        "Required navigation to a soft-deletable entity",
        "Required navigation '{1}' on '{0}' references '{2}', which is [SoftDelete]. EF Core turns a required " +
        "navigation into an INNER JOIN, so soft-deleting a '{2}' also hides every '{0}' row that points to it " +
        "from any query that joins '{1}' — the rows stay in the database but become invisible, and '{0}' is not " +
        "itself soft-deletable, so they cannot be restored",
        "Pick the behaviour you mean: make the navigation optional (Required = false on the [Relation.*] " +
        "attribute) so EF Core emits a LEFT JOIN and the dependent survives with a null reference; or drop " +
        "[SoftDelete] from the target; or add IgnoreQueryFilters() to the queries that must still see the rows. " +
        "If the dependent really is part of the target's aggregate, declare the relationship from the parent " +
        "with [Relation.OneToMany<T>] — an owning parent hiding its children is intended and not reported.");

    /// <summary>
    ///     <c>[Filter(Operator = FilterOperator.Between)]</c>. The operator is in the enum and no
    ///     generator renders it: every switch that maps an operator to an expression ends in a default
    ///     that emits <c>==</c>, so the filter compares for equality — and when the property is the
    ///     collection a range needs, the generated file does not compile at all.
    ///     <para>
    ///     An error rather than a warning: there is no reading of <c>Between</c> under which the
    ///     generated code is what the author asked for.
    ///     </para>
    /// </summary>
    public static readonly DiagnosticDescriptor BetweenOperatorNotSupported = DiagnosticFactory.Error(
        "PRAG0701",
        "FilterOperator.Between is not implemented",
        "Property '{0}' on '{1}' declares Operator = FilterOperator.Between, which no generator renders: "
        + "the generated filter falls back to equality, and on a collection property it does not compile",
        "Express the range as two properties over the same column: one with "
        + "Operator = FilterOperator.GreaterOrEqual and one with Operator = FilterOperator.LessOrEqual, "
        + "both carrying the same MapTo.");

    /// <summary>
    ///     <c>[CascadeOn&lt;TSource&gt;]</c> on a target entity that has no foreign key back to the
    ///     source. The generated handler finds its rows with <c>Where(e =&gt; e.{Source}Id ==
    ///     entityId)</c>, so without that property there is nothing to filter on.
    /// </summary>
    public static readonly DiagnosticDescriptor CascadeTargetWithoutForeignKey = DiagnosticFactory.Error(
        "PRAG0702",
        "[CascadeOn] target has no foreign key to the source",
        "'{0}.{1}' cascades from '{2}', but '{0}' declares no '{3}' property — the generated handler "
        + "has no way to find the rows that belong to a given '{2}'",
        "Add the foreign key the convention names ({3}), or declare the relationship with "
        + "[Relation.ManyToOne<{2}>] so it is generated.");

    /// <summary>
    ///     An attribute argument the generator parses and no template consumes. The declaration reads
    ///     as configuration and changes nothing, which is the failure mode hardest to notice: the build
    ///     is green, the code is generated, and the behaviour is the default.
    /// </summary>
        // A query input becomes a filter when it carries [Filter], when it is `required`, or when it is
    // nullable. A plain non-nullable scalar is none of the three: Apply returns the query untouched,
    // ToSpecification returns Spec.True, and the value the caller sent is read and dropped.
    //
    // Measured on a consumer application: `[Query<KnowledgeItem, …>(Single = true)]` with
    // `public Guid Id { get; init; }` and a route of `terms/{id}` answered 200 with whichever row came
    // first. It compiles, it returns a body of the right shape, and only an assertion that compares the
    // row to the one that was asked for can tell. Error, because there is no reading of a dropped input
    // that is correct — and the two forms that work are one word away.
    // [GenerateHierarchy] finds the parent by name: ParentId, or {TypeName}ParentId. There is no
    // argument to name a different property, and an entity whose key is ManagerId or ReportsToId — an
    // ordinary way to model the same tree — matched neither. The transform answered null, the pipeline
    // filtered it out, and the attribute produced no GetDescendants, no GetAncestors and no word.
    public static readonly DiagnosticDescriptor HierarchyHasNoParentKey = DiagnosticFactory.Error(
        "PRAG0708",
        "[GenerateHierarchy] finds no self-referencing relation",
        "'{0}' declares [GenerateHierarchy] and no [Relation.ManyToOne<{0}>], so there is no edge to "
        + "walk and no hierarchy query is generated",
        "The tree is a declared relation from the entity to itself; the attribute reads the parent key "
        + "from it. Declare [Relation.ManyToOne<TSelf>.WithNavigation(\"Parent\", Required = false)] — "
        + "a hand-written ParentId is not a relation.");

    /// <summary>
    ///     PRAG0713: several self-referencing relations, and <c>Via</c> does not say which is the tree.
    /// </summary>
    /// <remarks>
    ///     <c>Parent</c>, <c>MergedInto</c> and <c>Supersedes</c> are three relations to the same type,
    ///     and the tree is one of them. There is nothing to guess; there is a name to write.
    /// </remarks>
    public static readonly DiagnosticDescriptor HierarchyViaRequired = DiagnosticFactory.Error(
        "PRAG0713",
        "[GenerateHierarchy] needs Via",
        "'{0}' declares more than one relation to itself ({1}), so [GenerateHierarchy] has to say which "
        + "one is the tree: [GenerateHierarchy(Via = \"...\")]",
        "With several self-referencing relations only the navigation name tells the tree from the other "
        + "edges.");

    /// <summary>PRAG0714: <c>Via</c> names a navigation that is not a self-referencing relation.</summary>
    public static readonly DiagnosticDescriptor HierarchyViaNotFound = DiagnosticFactory.Error(
        "PRAG0714",
        "[GenerateHierarchy] Via does not resolve",
        "'{0}' has no self-referencing relation whose navigation is \"{1}\" — name one of: {2}",
        "Via is matched against the navigation names of the entity's [Relation.ManyToOne<TSelf>] "
        + "declarations.");

    /// <summary>PRAG0715: the tree's edge is required, so no row could be a root.</summary>
    public static readonly DiagnosticDescriptor HierarchyEdgeRequired = DiagnosticFactory.Error(
        "PRAG0715",
        "A hierarchy's edge must be optional",
        "The relation \"{1}\" on '{0}' is required, so every row must have a parent and no root can "
        + "exist — declare it with Required = false",
        "A tree has a root, and a root has no parent: the self-referencing foreign key has to be "
        + "nullable.");

    // [BindSpecification] lifts PRAG0707 by claiming the value has a consumer the transform cannot see.
    // The claim is worth exactly as much as the specification it points at, and nothing pointed at it:
    // mark an input, then delete the specification or never write it, and the value is read and dropped
    // again — with a declaration standing over it saying otherwise, which is quieter than the silence
    // the attribute exists to lift.
    //
    // The half that can be checked is checked here: a query cannot claim a specification reads its
    // inputs and then have no specification at all. The other half — an input marked but not actually
    // read by the specification's body — would take reading that body, which is too clever to trust.
    // PRAG0712 ([Query] on a type that is not partial) is the companion analyzer's, which reports it on
    // the declaration (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    public static readonly DiagnosticDescriptor BindSpecificationWithoutSpecification = DiagnosticFactory.Error(
        "PRAG0709",
        "[BindSpecification] with no specification to feed",
        "'{0}' on '{1}' is marked [BindSpecification], but '{1}' declares no Specification<T> property "
        + "for it to feed",
        "Add the specification the input builds — a get-only property of type Specification<TEntity>, "
        + "null when the rule does not apply — or drop the attribute and let the property be a filter.");

    public static readonly DiagnosticDescriptor QueryInputGeneratesNoFilter = DiagnosticFactory.Error(
        "PRAG0707",
        "Query input generates no filter",
        "Property '{0}' on '{1}' generates no filter: the value the caller sends is read and dropped",
        "A property becomes a filter when it carries [Filter], when it is 'required', or when it is "
        + "nullable. Write 'required {2}' for an input that is always supplied — a route id, say — or "
        + "'{2}?' for an optional one. Prefer 'required' on a mandatory scalar: the [Filter] form is "
        + "guarded by '!= default', so Guid.Empty or 0 reads as \"do not filter\" rather than \"look for "
        + "this value\".");

    public static readonly DiagnosticDescriptor DeclaredOptionNotHonoured = DiagnosticFactory.Warning(
        "PRAG0703",
        "This option is declared and has no effect",
        "{0} on '{1}' is read by the generator and consumed by nothing: {2}",
        "Remove the option, or use the shape that is implemented — the message names it.");

    /// <summary>
    ///     A <c>[Query&lt;TEntity, TResult&gt;]</c> whose result type has no <c>Projection</c>. The
    ///     generated <c>Apply</c> names <c>{TResult}.Projection</c>, and that member exists only on a
    ///     type carrying <c>[GenerateProjection]</c>: <c>[MapFrom&lt;T&gt;]</c> on its own produces
    ///     <c>FromEntity</c> and <c>Selector</c>, which run in memory.
    /// </summary>
    public static readonly DiagnosticDescriptor ResultTypeHasNoProjection = DiagnosticFactory.Error(
        "PRAG0704",
        "The query's result type has no Projection",
        "Query '{0}' projects '{1}', which declares no Projection: the generated Apply names "
        + "{1}.Projection and that member does not exist",
        "Put both attributes on the result type — [MapFrom<TEntity>] for the mapping and "
        + "[GenerateProjection] for the expression the query needs — and make sure the entity in "
        + "[MapFrom<...>] is the one the query reads.");

    // =========================================================================
    // Warnings (PRAG0710-0716) — Loading Profile & DTO analysis
    // =========================================================================

    /// <summary>
    ///     DTO references a navigation property that doesn't match any entity navigation,
    ///     so it won't be auto-included by the loading profile.
    /// </summary>
    public static readonly DiagnosticDescriptor DtoNavigationWithoutInclude = DiagnosticFactory.Warning(
        "PRAG0710",
        "DTO references navigation without Include",
        "DTO '{0}' has property '{1}' that looks like a navigation but doesn't match any navigation on entity '{2}'. " +
        "It won't be auto-included by [LoadWith]",
        "Properties that look like navigations (complex reference/collection types) but don't match " +
        "an entity navigation won't be included automatically. Verify the property name matches the entity navigation.");

    /// <summary>
    ///     Loading profile with MaxDepth > 3 may cause performance issues.
    /// </summary>
    public static readonly DiagnosticDescriptor DeepIncludeWithoutLoadWith = DiagnosticFactory.Warning(
        "PRAG0711",
        "Deep Include depth on loading profile",
        "DTO '{0}' has MaxDepth={1} on [LoadWith<{2}>]. " +
        "Include depth > 3 may cause performance issues — consider reducing depth or using Projection strategy",
        "Deep Include chains generate complex SQL with many JOINs. Consider using QueryStrategy.Projection " +
        "with explicit Select to load only the data you need.");

    /// <summary>
    ///     DTO with many navigation levels should consider Projection strategy.
    /// </summary>
    public static readonly DiagnosticDescriptor DtoWithManyNavigationLevels = DiagnosticFactory.Warning(
        "PRAG0716",
        "DTO with many navigation levels",
        "DTO '{0}' includes {1} navigation paths via [LoadWith<{2}>]. " +
        "Consider using QueryStrategy.Projection for better performance",
        "Loading many navigations generates complex queries. Consider using Projection strategy with " +
        "explicit Select to load only the columns you need, or split into multiple queries.");

    /// <summary>
    ///     A <c>[Count&lt;T&gt;(Where = …)]</c> clause that never mentions the row it filters.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The clause is the <b>body of a lambda</b>: it is inserted verbatim into
    ///         <c>g.Count(x =&gt; {clause})</c>, so it has to be written against <c>x</c>. A clause of bare
    ///         property names — which is what the attribute's own example showed — becomes
    ///         <c>x =&gt; Status == OrderStatus.Completed</c> and fails as <c>CS0103</c> inside
    ///         <c>{View}.QueryView.g.cs</c>: an error on a line the author never wrote, naming a symbol
    ///         that is in scope in their file and not in the generated one.
    ///     </para>
    ///     <para>
    ///         ⚠️ The trap is an asymmetry inside one family: <c>Sum</c>, <c>Avg</c>, <c>Min</c> and
    ///         <c>Max</c> take a bare member path and the generator writes <c>x.{Expression}</c> itself.
    ///         Two conventions on four sibling attributes, and nothing said which was which.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor CountClauseDoesNotNameTheRow = DiagnosticFactory.Warning(
        "PRAG0722",
        "Count clause does not name the row",
        "Property '{0}' declares [Count<{1}>(Where = \"{2}\")], but the clause never mentions 'x'. " +
        "The clause is a lambda body over the row — write it as \"x.{2}\" — otherwise the generated " +
        "count does not compile (CS0103 in the generated query view)",
        "Unlike Sum/Avg/Min/Max, whose Expression is a bare member path the generator qualifies, " +
        "Where is inserted verbatim into g.Count(x => …) and must be written against x.");

    /// <summary>
    ///     A query takes a canonical <c>GridFilterRequest</c> for an entity that declares no bridge.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[GenerateGridBridge]</c> on the entity is the bridge's only trigger, and the generated
    ///         <c>Apply</c> calls that bridge by name. Without it the failure is a <c>CS0103</c> inside
    ///         <c>{Query}.Query.g.cs</c> — an error on a line the author never wrote, naming a type they
    ///         have never heard of.
    ///     </para>
    ///     <para>
    ///         An error rather than a warning: there is no degraded behaviour to fall back to. A query
    ///         that cannot apply the request would answer every row for every request, which is the
    ///         opposite of what a filtered grid asked for.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor GridRequestWithoutBridge = DiagnosticFactory.Error(
        "PRAG0723",
        "Grid request without a bridge",
        "Query '{0}' takes a canonical grid request in '{1}', but entity '{2}' does not declare " +
        "[GenerateGridBridge], so there is no {2}GridFilterBridge to apply it with",
        "Put [GenerateGridBridge] on the entity, and [Filterable] on each property the grid may name: " +
        "the bridge is an allowlist, so a property that does not declare itself filterable stays " +
        "unreachable from the wire.");

    /// <summary>
    ///     A query pages twice: once from the canonical request, once from its own Page/PageSize.
    /// </summary>
    /// <remarks>
    ///     The request carries <c>Page</c> and <c>PageSize</c> and the bridge applies them inside
    ///     <c>Apply</c>; the executor then applies the query's own on top of the result. The second
    ///     skip counts from the first page's rows, so page 2 of 2 answers nothing at all — and the total
    ///     the caller is handed counts the rows the first paging already threw away.
    /// </remarks>
    public static readonly DiagnosticDescriptor GridRequestPagesTwice = DiagnosticFactory.Warning(
        "PRAG0724",
        "Grid request pages twice",
        "Query '{0}' takes a canonical grid request in '{1}' and also declares Page/PageSize. The " +
        "request's own paging is applied by the bridge, and the query's is applied on top of it, so " +
        "the rows are paged twice",
        "Let the request carry the page — a grid sends it there — and drop Page/PageSize from the " +
        "query; or keep the query's paging and leave Page/PageSize unset on the request.");

    /// <summary>
    ///     A permission declared on a query names a constant nothing in the catalog matches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generated invoker enforces the permissions it was written with, so a name that did
    ///         not bind is a name it cannot enforce: the query would run its permission step, find
    ///         nothing to require, and answer every caller. That is worse than declaring nothing,
    ///         because the declaration reads as protection.
    ///     </para>
    ///     <para>
    ///         Usually the constant belongs to another assembly's permission class, or the entity it was
    ///         derived from is not in this compilation. A string literal always binds.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     Two specifications in one namespace that would derive the same query type.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An <b>error</b>, and the severity is the point. The derived type is <c>{member}Query</c> in
    ///     the container's namespace, so two containers sharing a namespace and a member name produce one
    ///     name twice — and two generated files with the same hint. Roslyn answers a duplicate hint by
    ///     discarding the <b>whole generator's output</b> with a <c>CS8785</c> that is only a warning:
    ///     without <c>--warnaserror</c> the build succeeds with every generated file missing. A warning
    ///     here would be drowned by exactly that; an error stops the build while the cause is still on
    ///     screen.
    ///     <para>
    ///     Refusing rather than renaming is deliberate: qualifying every derived type with its container
    ///     would change a public name for everybody to accommodate a case that is, said out loud,
    ///     ambiguous — two rules with one name in one namespace.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor DerivedQueryNameCollides = DiagnosticFactory.Error(
        "PRAG0726", "Two specifications derive the same query type",
        "Specifications '{0}' and '{1}' both derive '{2}'. Two generated files would carry one hint name, "
        + "and the whole generator's output is dropped when that happens",
        "Rename one of the specifications, or move one to a namespace of its own.");

    /// <summary>
    ///     <c>Paged = true</c> on a query that already carries <c>Page</c> and <c>PageSize</c>.
    /// </summary>
    /// <remarks>
    ///     The option asks the generator to write the two properties, and the generated half cannot:
    ///     a second declaration in the same partial class is <c>CS0102</c>. So the ask is dropped —
    ///     and a dropped ask that says nothing is how an author comes to believe an option is doing
    ///     something. A warning, not an error: the query pages either way, which is what was wanted.
    /// </remarks>
    public static readonly DiagnosticDescriptor PagingRequestIsRedundant = DiagnosticFactory.Warning(
        "PRAG0727",
        "Paged = true on a query that already pages",
        "Query '{0}' declares Page and PageSize of its own, so 'Paged = true' adds nothing",
        "Remove 'Paged = true', or remove the two properties and let the generator write them.");

    /// <summary>
    ///     Two <c>[Published]</c> queries contribute the same method name to one read contract.
    /// </summary>
    /// <remarks>
    ///     The case the suffix stripping creates: <c>SearchInvoicesQuery</c> and <c>SearchInvoices</c>
    ///     are two types that now want one name. An error, and the contract is not emitted: writing
    ///     both members would be <c>CS0111</c> inside a generated file, which sends the author to code
    ///     they did not write and cannot edit.
    /// </remarks>
    public static readonly DiagnosticDescriptor PublishedQueryMethodNameCollides = DiagnosticFactory.Error(
        "PRAG0728",
        "Two published queries contribute the same contract method name",
        "Queries '{0}' and '{1}' both contribute '{2}' to '{3}', so the contract cannot declare both",
        "Give one of them an explicit name: [Published(MethodName = \"...\")].");

    /// <summary>
    ///     A <c>[Query]</c> written on a member that no query can be derived from.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The transform can decline a member six different ways; answered with a bare <c>null</c>,
    ///     the attribute would compile, nothing would be generated, and the build would be green. A
    ///     declaration that is not honoured has to say so: the message names the member and what is
    ///     wrong with it.
    ///     <para>
    ///         A warning rather than an error: the surrounding code still compiles and the author may
    ///         be mid-edit. What must not happen is silence.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor SpecificationQueryDerivesNothing = DiagnosticFactory.Warning(
        "PRAG0729",
        "The [Query] on this member derives nothing",
        "'{0}' carries [Query] but no query is derived from it: {1}",
        "A derived query reads a static member that returns Specification<TEntity>, declared in a "
        + "non-generic type. Move the rule to one, or remove the attribute.");

    public static readonly DiagnosticDescriptor QueryPermissionDoesNotResolve = DiagnosticFactory.Error(
        "PRAG0725",
        "Query permission does not resolve",
        "Query '{0}' requires permission '{1}', which no generated permission constant matches, so " +
        "the query's invoker would enforce nothing",
        "Name a constant this compilation produces, or write the permission as a string literal.");

    /// <summary>
    ///     An <c>[EagerLoad]</c> path of a query with a segment that is not a navigation of its entity. The path is not
    ///     emitted — it was an EF Core exception at the first request. The mutation's twin is PRAG0453.
    /// </summary>
    public static readonly DiagnosticDescriptor EagerLoadPathNamesNoNavigation = DiagnosticFactory.Error(
        "PRAG0736",
        "[EagerLoad] path names no navigation",
        "[EagerLoad] on '{0}': '{1}' — '{2}' is not a navigation of the entity it is read on",
        "An [EagerLoad] path is a chain of navigations — declared, or generated from a [Relation] — each read on the "
        + "entity the previous one leads to. A segment that names nothing, or names a scalar, would be refused by "
        + "EF Core at the first request.");

    /// <summary>
    ///     A <c>[Join&lt;T&gt;(Via = …)]</c> whose path is not a navigation of the query's entity. The
    ///     path is not emitted: copied into the generated <c>Apply</c> unread, it would give the author
    ///     a <c>CS1061</c> at a line of a file they did not write — three of them, on the
    ///     <c>Include</c>, the filter and the specification. The twin for <c>[EagerLoad]</c>
    ///     is PRAG0736, and it is the same resolver.
    /// </summary>
    public static readonly DiagnosticDescriptor JoinPathNamesNoNavigation = DiagnosticFactory.Error(
        "PRAG0737",
        "[Join] Via names no navigation",
        "[Join<{1}>(Via = \"{2}\")] on '{0}': '{3}' is not a navigation of the entity it is read on",
        "Via is a chain of navigations — declared, or generated from a [Relation] — each read on the entity the "
        + "previous one leads to. ⚠️ A cross-boundary [Relation] generates the foreign key and no navigation "
        + "property, so the target is not reachable by name from this side: name a navigation that exists, or load "
        + "the other side through its own query.");

    /// <summary>
    ///     A key join on a query whose result <b>is</b> the entity. There is nothing to project into,
    ///     so the joined columns would have nowhere to go.
    /// </summary>
    public static readonly DiagnosticDescriptor KeyJoinNeedsAResultType = DiagnosticFactory.Error(
        "PRAG0738",
        "A key join needs a result type",
        "[Join<{1}>(ForeignKey = …)] on '{0}': the query answers with '{1}' itself, so the joined entity's columns "
        + "have nowhere to go",
        "A key join reaches an entity no navigation leads to, and delivers its columns through the query's result "
        + "type. Declare [Query<TEntity, TResult>] with a result that names them, or drop the join — as a filter on "
        + "the root alone it says no more than a [Filter] does.");

    /// <summary>
    ///     A key join whose <c>ForeignKey</c> or <c>TargetKey</c> names no property. The same rule
    ///     <c>Via</c> follows, for the same reason.
    /// </summary>
    public static readonly DiagnosticDescriptor JoinKeyNamesNoProperty = DiagnosticFactory.Error(
        "PRAG0739",
        "[Join] key names no property",
        "[Join<{1}>] on '{0}': '{2}' is not a property of {3}",
        "ForeignKey is read on the query's entity and TargetKey on the joined type. ⚠️ A foreign key that a "
        + "[Relation] generates is not visible to the generator that writes it — and if a [Relation] exists there is "
        + "a navigation, so reach the target with Via instead.");

    /// <summary>
    ///     A result property that neither the entity nor any joined target answers.
    /// </summary>
    public static readonly DiagnosticDescriptor JoinedResultPropertyHasNoSource = DiagnosticFactory.Error(
        "PRAG0740",
        "A joined result property has no source",
        "'{0}' projects into '{1}', whose property '{2}' is on neither the entity nor any joined target",
        "A joined step builds the result property by property. Name it as the entity spells it, or prefix it with "
        + "the joined type — CustomerName for Customer.Name — or with the join's Alias when two joins reach the same "
        + "type. Without this the author would read a CS0117 inside a generated file.");

    /// <summary>
    ///     A join type EF Core cannot translate.
    /// </summary>
    public static readonly DiagnosticDescriptor JoinTypeCannotBeGenerated = DiagnosticFactory.Error(
        "PRAG0741",
        "This join type cannot be generated",
        "[Join<{1}>(Type = JoinType.{2})] on '{0}': EF Core cannot translate it, and an inner join wearing its name "
        + "would answer with fewer rows than it promises",
        "Inner, Left and Cross generate. Full has no LINQ spelling EF Core turns into a FULL OUTER JOIN. Right is "
        + "Left with the operands swapped, and the step receives the root set already filtered, sorted and paged — "
        + "so rows of the target the root's filters never selected cannot be added back. Swap the query's entity and "
        + "its join to say the same thing.");

    /// <summary>
    ///     A key join whose target is in no <c>DbContext</c> this query can reach.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The runtime already refuses it, naming the declaration to add — but the first
    ///         <b>request</b> is what says so, not the build. A declaration that compiles and fails at
    ///         run time is what this family of diagnostics exists to close.
    ///     </para>
    ///     <para>
    ///         ⚠️ Answerable here, in the module, although the boundary's <c>DbContext</c> is generated
    ///         by the host: what decides that context's content is two author-written declarations —
    ///         <c>[BelongsTo]</c>/<c>[Owns]</c> and <c>[ReadAccess&lt;T&gt;]</c> on the boundary class —
    ///         and both sit beside the query.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor JoinTargetIsOutsideTheBoundary = DiagnosticFactory.Error(
        "PRAG0742",
        "The joined entity is not in this boundary's model",
        "[Join<{1}>(ForeignKey = …)] on '{0}': {1} belongs to another boundary and '{2}' does not read it, so there "
        + "is no set to join against",
        "EF Core composes a join only inside one DbContext instance, and a host builds one per boundary. Add "
        + "[ReadAccess<T>] to the boundary — which is what puts the other boundary's DbSet in this model — or reach "
        + "the target through its own query. ⚠️ [ReadAccess] also requires the two boundaries to share a database; "
        + "PRAG0706 reports it when they do not.");
}
