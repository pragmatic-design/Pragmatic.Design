using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>What the generator reports: every call site outside the translated subset says why, and what to do instead.</summary>
internal static class Diagnostics
{
    private const string Category = "Pragmatic.Persistence.Sql";

    public static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "PRAG3001",
        "LINQ operator not translated to SQL",
        "'{0}' is not in the translated subset: {1}. The query would run nothing in memory; rewrite it with the operators the subset has, or move it to a declared [Query].",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedExpression = new(
        "PRAG3002",
        "Expression not translated to SQL",
        "'{0}' ({1}) cannot be translated: {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>The key of the property a PRAG3002 carries when the expression is a value captured from outside the lambda: the captured expression's text, for the code fix that rewrites the lambda into the two-parameter form.</summary>
    public const string CaptureProperty = "capture";

    /// <summary>
    ///     PRAG3002 for a predicate or key the translator refused: when the reason is a captured value, the diagnostic carries the captured
    ///     expression as a property (<see cref="CaptureProperty" />), so the IDE's code fix knows what to turn into the argument.
    /// </summary>
    public static Diagnostic Untranslated(Location where, string expression, string what, string why)
    {
        var properties = why.Contains("value from outside the lambda") ? System.Collections.Immutable.ImmutableDictionary<string, string?>.Empty.Add(CaptureProperty, expression) : null;
        return Diagnostic.Create(UnsupportedExpression, where, properties, expression, what, why);
    }

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        "PRAG3003",
        "Entity property type not mapped",
        "Property '{0}' of entity '{1}' has type '{2}', which the reader does not map; mark it [NotMapped], use a mapped type (Guid, string, int, long, decimal, double, bool, DateTimeOffset, DateTime, DateOnly, TimeOnly, TimeSpan, byte[], enums, and their nullable forms), or give it a [Convert<TConverter>] to one of them",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NavigationWithoutKey = new(
        "PRAG3004",
        "Navigation without a foreign key",
        "'{0}.{1}' is a navigation to entity '{2}' but '{0}' has no property '{1}Id' to join on",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChainTypedAsQueryable = new(
        "PRAG3005",
        "Query chain translated at runtime",
        "'{0}' binds to {1} because the chain is typed as IQueryable<T>: it is translated at runtime from the expression tree (the same subset, no compile-time check). Keep the chain typed SqlQuery<T> (var) up to the terminal to translate it here.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryShape = new(
        "PRAG3006",
        "[Query] definition not in the declared shape",
        "'{0}' cannot be generated: {1}. A [Query] is a static method of a static partial class, takes a SqlQuery<T> first, and its body is one chain on that parameter ending in a terminal.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompositeKey = new(
        "PRAG3007",
        "Composite key where a single-column key is needed",
        "'{0}' has a composite key ({1}): {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HierarchyShape = new(
        "PRAG3008",
        "[GenerateHierarchy] without a tree to walk",
        "'{0}' cannot generate its hierarchy queries: {1}. The entity needs a navigation to itself with its foreign key (Parent, ParentId); with several, Via names the one that is the tree.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeysetNotTotal = new(
        "PRAG3009",
        "Keyset ordering not total",
        "'{0}' is not the key of {1}: a cursor over an ordering that is not total may skip or repeat rows. End the ordering with ThenBy(x => x.{2}) and put its value last in the cursor.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryInLoop = new(
        "PRAG3010",
        "Query inside a loop over rows",
        "'{0}' runs once per '{1}' ({2}): one query per row of another query's result (N+1). Load the related rows with the parents (Include, a DTO with a collection, or one query with an IN list over the keys), or do the loop's work in one query.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnindexedPredicate = new(
        "PRAG3011",
        "Predicate on a column without an index",
        "'{0}' of {1} has no index: a WHERE on it scans the table. Add the index to the schema (the committed *.schema.json; [Indexed] on the property where no snapshot lists the table), or filter on an indexed column first.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Emulated = new(
        "PRAG3012",
        "Function emulated on a dialect",
        "{0} on {1} is an emulation: {2}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FunctionShape = new(
        "PRAG3013",
        "[SqlFunction] not in the declared shape",
        "'{0}' cannot be generated: {1}. A [SqlFunction] is a static partial method of a static partial class, with a private static SqlFragment definition of the same name (the dialect first, then one SqlFragment per argument) whose body is a switch on the dialect built with Fragment.*.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InheritanceShape = new(
        "PRAG3014",
        "[Inheritance] hierarchy not in the mapped shape",
        "'{0}' cannot be mapped as a hierarchy: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CensusStale = new(
        "PRAG3015",
        "The committed query census is stale",
        "{0} does not match the queries this build translated ({1}): build with the Census target (PragmaticCensusFile set) to rewrite it, and commit it with the change",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaFileInvalid = new(
        "PRAG3016",
        "A schema snapshot could not be read",
        "'{0}' is not a schema snapshot ({1}): the index analysis does not read it; a snapshot is the JSON the migrations write (tables[].name, columns[].isPrimaryKey, indexes[].columns)",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RawStatementMismatch = new(
        "PRAG3026",
        "Hand-written statement does not match the model",
        "The hand-written statement does not match the model: {0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnboundedRead = new(
        "PRAG3021",
        "Unbounded read",
        "'{0}' reads every row of {1} the chain matches, however many: bound it with Take (page with After/Before), stream it with AsAsyncEnumerable, or mark a lookup table [Table(Small = true)]",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LeadingWildcard = new(
        "PRAG3022",
        "LIKE with a leading wildcard on an indexed column",
        "'{0}' of {1} has an index, but '{2}' is a LIKE that starts with a wildcard, which no B-tree index serves: the WHERE scans the table. StartsWith uses the index; otherwise a trigram or full-text index.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstantPredicate = new(
        "PRAG3023",
        "Constant predicate",
        "The predicate '{0}' does not read the row: it is true for every row or for none (it filters nothing, or everything)",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeReadOnce = new(
        "PRAG3024",
        "Include read for one column",
        "Include({0}) loads the whole {1} row with every {2}, and this method reads only its '{3}': project it instead (Select(x => new {{ …, x.{0}.{3} }})), or read the key",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor WritesOutsideTransaction = new(
        "PRAG3025",
        "Several writes outside a transaction",
        "'{0}' runs {1} writes with no transaction around them: a failure between two leaves the first applied. Wrap them in BeginTransaction/TransactAsync, or put them in one WriteBatch.",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaShape = new(
        "PRAG3027",
        "A declared database's schema cannot be emitted",
        "The schema of {0} cannot describe {1}: {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntityWithoutKey = new(
        "PRAG3028",
        "An entity has no key",
        "{0} has no key: mark the key property [Key], or name it Id, or PersistenceId as Pragmatic's [Entity] does; a table read and never written is [Table(ReadOnly = true)]",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChainOverHiddenShape = new(
        "PRAG3029",
        "A chain reads members no referenced engine declares",
        "'{0}' on {1} does not bind for the Sql engine: it reads members of a Pragmatic entity that Pragmatic's generator declares only for an engine the compilation references, and none is referenced. Reference the Sql engine's bridge (Pragmatic.Persistence.Sql), or the chain runs nothing.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryNotCompiled = new(
        "PRAG3033",
        "A declared query of a Pragmatic entity is not compiled for the Sql engine",
        "{0} is not compiled for the Sql engine: {1}. IReadRepository.RunAsync refuses it on the Sql engine; EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GridPartlyCompiled = new(
        "PRAG3034",
        "A grid adapter of a Pragmatic entity is compiled for the Sql engine without part of its declaration",
        "{0} is compiled for the Sql engine without {1}. EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ScopeRuleNotCompiled = new(
        "PRAG3035",
        "A data scope rule of a Pragmatic entity is not compiled for the Sql engine",
        "The data scope rule {0} is not compiled for the Sql engine: {1}. Registered and Computed or Hybrid, it is refused at read; EF Core runs it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor LoadIncludeNotCompiled = new(
        "PRAG3036",
        "A load with Include of a Pragmatic entity is not compiled for the Sql engine",
        "[{0}<{1}>] on {2} loads with Include, which the Sql engine does not compile here: {3}. A load with Include is compiled for a load by key of an entity of the same assembly; read the navigation with a second load otherwise.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     An error: the Sql engine's unit of work tracks each row it reads, not a graph, so the children a mutation writes through its entity
    ///     (merged into it, or reached by a soft delete that cascades) would not be saved. Refused at build rather than written for nothing.
    /// </summary>
    public static readonly DiagnosticDescriptor MutationWritesChildren = new(
        "PRAG3037",
        "A mutation of a Pragmatic entity writes children on the Sql engine",
        "{0} writes the children of {1} ({2}), which the Sql engine's unit of work does not save: it saves each row it tracks, not a graph. Write each child with a mutation of its own.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InGeneratedSource = new(
        "PRAG3032",
        "A chain the Sql engine generated did not translate",
        "In {0}, which the Sql engine generates: {1} {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SpecificationNotCompiled = new(
        "PRAG3031",
        "A specification of a Pragmatic entity is not compiled for the Sql engine",
        "Spec<{0}>.Where is not compiled for the Sql engine: {1}. A read through the Sql engine refuses a specification built on it; EF Core reads it as before.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RuleReadsTheMoment = new(
        "PRAG3030",
        "A visibility rule of a Pragmatic entity reads a value of the moment",
        "The rule {0} of {1} reads a value of the moment (the caller, the clock): a rule of a Pragmatic entity is a fixed predicate on the row, built once (PRAG0718 on EF Core). Who is asking is the filter families' to decide: [HasOwner], [HasAccessScopes], ITenantEntity, lifted by the mode and the bypass permission.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Translated = new(
        "PRAG3099",
        "LINQ call site translated to SQL",
        "{0} → {1}",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true);

    /// <summary>True for the reports a census build promotes to warnings: the translated call sites and the index analysis (the command-line compiler shows no Info diagnostics).</summary>
    public static bool IsCensus(Diagnostic diagnostic) => diagnostic.Id == Translated.Id || diagnostic.Id == UnindexedPredicate.Id || diagnostic.Id == Emulated.Id
        || diagnostic.Id == UnboundedRead.Id || diagnostic.Id == LeadingWildcard.Id || diagnostic.Id == ConstantPredicate.Id || diagnostic.Id == IncludeReadOnce.Id || diagnostic.Id == WritesOutsideTransaction.Id;

    /// <summary>The same report as a warning, so a census build (`-p:PragmaticCensus=true`) prints every call site with its SQL and its scans.</summary>
    public static Diagnostic AsCensus(Diagnostic translated)
        => Diagnostic.Create(translated.Id, Category, translated.GetMessage(System.Globalization.CultureInfo.InvariantCulture), DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, isEnabledByDefault: true, warningLevel: 1, location: translated.Location);
}
