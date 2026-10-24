using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;
using Pragmatic.SourceGen;
using ShapeSourceModel = Pragmatic.SourceGenerator.Features.Persistence.Models.ShapeSourceModel;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     Translates every query chain written on <c>SqlQuery&lt;T&gt;</c> at compile time. The chain is read
///     from its terminal (<c>ToListAsync</c>, <c>CountAsync</c>, <c>ToSql</c>…) back to <c>Query&lt;T&gt;()</c>,
///     through local variables, and becomes one <c>SqlPlan</c>: the SQL for each dialect, the parameters'
///     types, how to bind them from the lambdas the chain stored in its slots, how to read a row. The
///     operators in between are intercepted too, only to store their predicate (<c>Where</c>) or to retype
///     the chain (<c>Select</c>); <c>Skip</c> and <c>Take</c> run as they are and become parameters. Anything
///     outside the subset is a diagnostic at the call site, with the remedy; so is a chain typed as
///     <c>IQueryable&lt;T&gt;</c>, whose operators bind to System.Linq and would fail at runtime.
/// </summary>
/// <remarks>
///     A feature of the unified generator (<see cref="Register" />): it does its work only in a compilation that references the Sql engine,
///     so a consumer without it pays the syntax scan and nothing else.
/// </remarks>
internal static partial class SqlInterceptorGenerator
{
    private const string SqlSession = "Pragmatic.Persistence.Sql.SqlSession";

    /// <summary>True when the compilation references the Sql engine's runtime: what every step checks before reading a symbol.</summary>
    private static bool UsesSql(Compilation compilation) => compilation.GetTypeByMetadataName(SqlSession) is not null;

    private static readonly HashSet<string> Operators = ["Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Select", "Project", "Include", "GroupBy", "After", "Before", "AsOf", "Matching", "OrderedBy", "Distinct", "Union", "Concat", "Except", "Intersect", "Join", "LeftJoin", "GroupJoin", "SelectMany", "OfType", "DistinctOn"];

    /// <summary>The set operations: their argument is a second chain on the same entity, a member of the statement.</summary>
    internal static bool IsSetOperation(string name) => name is "Union" or "Concat" or "Except" or "Intersect";

    /// <summary>The explicit joins: their first argument is a chain on another entity, joined under its own alias.</summary>
    internal static bool IsJoin(string name) => name is "Join" or "LeftJoin" or "GroupJoin";

    /// <summary>The operators that open a joined row: the joins, and a SelectMany (its result selector is the row from there on).</summary>
    internal static bool OpensRow(string name) => IsJoin(name) || name == "SelectMany";

    /// <summary>A declared chain: a <c>[Query]</c> method that returns <c>SqlQuery&lt;T&gt;</c> instead of running a terminal; called in place, its steps are inlined where it is used.</summary>
    internal static bool IsFragment(IMethodSymbol method)
        => method.ReturnType is INamedTypeSymbol { Name: "SqlQuery", TypeArguments.Length: 1 } && method.GetAttributes().Any(a => a.AttributeClass?.Name == "QueryAttribute");

    /// <summary>The deferred terminals, recorded on a <c>SqlBatch</c>, and the async twin whose plan they take.</summary>
    private static readonly Dictionary<string, string> BatchTerminals = new() { ["ToList"] = "ToListAsync", ["FirstOrDefault"] = "FirstOrDefaultAsync", ["Count"] = "CountAsync", ["Any"] = "AnyAsync" };

    private static readonly HashSet<string> Terminals = ["ToList", "FirstOrDefault", "Count", "Any", "ToListAsync", "ToArrayAsync", "ShapeAsync", "AsAsyncEnumerable", "ToDictionaryAsync", "FirstOrDefaultAsync", "FirstAsync", "SingleAsync", "SingleOrDefaultAsync", "CountAsync", "AnyAsync", "SumAsync", "MinAsync", "MaxAsync", "AverageAsync", "ExecuteUpdateAsync", "ExecuteDeleteAsync", "ExecuteInsertAsync", "SyncAsync", "ToSql", "ToCommand"];

    private static readonly HashSet<string> Aggregates = ["SumAsync", "MinAsync", "MaxAsync", "AverageAsync"];

    /// <summary>Operators of other providers that a chain typed as IQueryable&lt;T&gt; would bind to, which the generator reports instead of letting them fail at runtime.</summary>
    private static readonly HashSet<string> ForeignContainers = ["System.Linq.Queryable", "System.Linq.Enumerable", "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"];

    private const int MaxSlots = 8; // SqlQuery.MaxSlots

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    /// <summary>Fully qualified, with the nullable annotations of reference types: the form an interceptor's parameters must repeat.</summary>
    internal static readonly SymbolDisplayFormat Annotated = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The Sql engine's generation, registered by <c>PragmaticSourceGenerator</c>.</summary>
    internal static void Register(IncrementalGeneratorInitializationContext context, IncrementalValueProvider<EquatableArray<ShapeSourceModel>> shapeSources)
    {
        var entities = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Pragmatic.Persistence.Sql.EntityAttribute",
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        // Every member call of every file reaches the transform: the binding is skipped where the compilation has no Sql engine.
        var calls = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax },
            static (ctx, ct) => UsesSql(ctx.SemanticModel.Compilation) ? CallSite.TryCreate(ctx, ct) : null);

        var querySyntax = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is QueryExpressionSyntax,
            static (ctx, ct) => UsesSql(ctx.SemanticModel.Compilation) ? QuerySyntaxOverSql((QueryExpressionSyntax)ctx.Node, ctx.SemanticModel, ct) : null);
        context.RegisterSourceOutput(querySyntax.Where(static l => l is not null), static (spc, location) => spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedMethod, location, "query syntax", QuerySyntaxReason)));

        var queries = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Pragmatic.Persistence.Sql.QueryAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol);

        var dtos = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Pragmatic.Persistence.Sql.MapFromAttribute`1",
            static (node, _) => node is TypeDeclarationSyntax, // a class, a record, or a record struct (no object per row)
            static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol);

        // `dotnet build -p:PragmaticCensus=true` (the property made compiler-visible by the consuming project) turns the hidden
        // "translated" diagnostics into infos: the build log becomes the census of every call site and its SQL.
        var census = context.AnalyzerConfigOptionsProvider.Select(static (options, _) => options.GlobalOptions.TryGetValue("build_property.PragmaticCensus", out var value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var functions = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Pragmatic.Persistence.Sql.SqlFunctionAttribute",
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => (IMethodSymbol)ctx.TargetSymbol);

        // The committed census (queries.census.json among the project's AdditionalFiles): compared with the fresh one, PRAG3015 when stale.
        var committed = context.AdditionalTextsProvider.Where(static text => string.Equals(System.IO.Path.GetFileName(text.Path), CensusFileName, StringComparison.OrdinalIgnoreCase)).Select(static (text, ct) => text.GetText(ct)?.ToString()).Collect();

        // The schema snapshots (*.schema.json among the AdditionalFiles, as the repository's migrations write them): the indexes the analysis reads instead of [Indexed].
        var schemas = context.AdditionalTextsProvider.Where(static text => text.Path.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)).Select(static (text, ct) => (text.Path, text.GetText(ct)?.ToString())).Collect();

        var combined = calls.Where(static c => c is not null).Collect().Combine(entities.Collect()).Combine(queries.Collect()).Combine(dtos.Collect()).Combine(functions.Collect()).Combine(context.CompilationProvider).Combine(census).Combine(committed).Combine(schemas).Combine(shapeSources);
        context.RegisterSourceOutput(combined, static (spc, all) => { var (source, shape) = all; Emit(spc, source.Left.Left.Left.Left.Left.Left.Left.Left!, source.Left.Left.Left.Left.Left.Left.Left.Right, source.Left.Left.Left.Left.Left.Left.Right, source.Left.Left.Left.Left.Left.Right, source.Left.Left.Left.Left.Right, source.Left.Left.Left.Right, source.Left.Left.Right, source.Left.Right.FirstOrDefault(), source.Right, shape); });
    }

    /// <summary>One operator of a chain, in the order the chain applies them.</summary>
    internal sealed class Step
    {
        public Step(string name, InvocationExpressionSyntax invocation, IMethodSymbol method)
        {
            Name = name;
            Invocation = invocation;
            Method = method;
            Lambda = invocation.ArgumentList.Arguments.Count > 0 ? invocation.ArgumentList.Arguments[0].Expression as LambdaExpressionSyntax : null;
            Arg = TakesArg(method) && invocation.ArgumentList.Arguments.Count > 1 ? invocation.ArgumentList.Arguments[1].Expression : null;
        }

        public string Name { get; }

        public InvocationExpressionSyntax Invocation { get; }

        public IMethodSymbol Method { get; }

        public LambdaExpressionSyntax? Lambda { get; }

        /// <summary>The argument of the two-parameter form (<c>Where((o, a) => …, arg)</c>): stored in a slot of the chain.</summary>
        public ExpressionSyntax? Arg { get; }

        /// <summary>True when the step stores a value in a slot: the argument form of a predicate, or a cursor (<c>After</c>).</summary>
        public bool UsesSlot => Arg is not null || IsCursor || Name is "AsOf" or "Matching" or "OrderedBy";

        /// <summary>After or Before: the cursor operators, one slot for the cursor.</summary>
        public bool IsCursor => Name is "After" or "Before";

        /// <summary>A set operation's or a join's other chain walked to its root, or a declared chain's body; null when it could not be read (reported at the call site).</summary>
        public List<Step>? Inner { get; set; }

        /// <summary>The model the step's syntax binds in, when it is not the chain's (a declared chain's body lives in its own file).</summary>
        public SemanticModel? Model { get; set; }

        /// <summary>A declared chain called in place: one slot per parameter after the chain.</summary>
        public bool IsFragment => Name == "Fragment";

        /// <summary>The slots the step takes in the chain: its own, a set operation member's or a join's inner chain's, a declared chain's parameters.</summary>
        public int SlotCount => IsFragment ? Method.Parameters.Length - 1 : Inner is not null ? Inner.Sum(s => s.SlotCount) : UsesSlot ? 1 : 0;
    }

    /// <summary>True for the overloads that take the lambda's values as an argument (<c>Where&lt;TArg&gt;</c>, <c>CountAsync&lt;TArg&gt;</c>…).</summary>
    internal static bool TakesArg(IMethodSymbol method) => method.OriginalDefinition.TypeParameters.Any(t => t.Name == "TArg");

    private static void Emit(SourceProductionContext spc, ImmutableArray<CallSite?> calls, ImmutableArray<INamedTypeSymbol> entityTypes, ImmutableArray<IMethodSymbol> queries, ImmutableArray<INamedTypeSymbol> dtos, ImmutableArray<IMethodSymbol> functionMethods, Compilation compilation, bool census, string? committedCensus, ImmutableArray<(string Path, string? Text)> snapshots, EquatableArray<ShapeSourceModel> shapeSources)
    {
        if (!UsesSql(compilation))
            return;
        var input = new HashSet<SyntaxTree>(compilation.SyntaxTrees); // the trees a diagnostic may be reported in: not the ones added below
        WithPragmaticShape(spc, shapeSources, ref compilation, ref calls, ref entityTypes, ref queries, ref dtos, ref functionMethods); // Pragmatic's entities without EF Core: their shape, the chains bound on it
        var diagnostics = new List<Diagnostic>();
        var censusEntries = new List<CensusEntry>(); // every translated terminal and [Query], for the census file
        var functions = ReadFunctions(functionMethods, compilation, diagnostics); // the application's [SqlFunction]s: implemented and registered below
        var schema = ReadSchema(snapshots, diagnostics);
        var entities = new Dictionary<INamedTypeSymbol, EntityModel?>(SymbolEqualityComparer.Default);
        foreach (var type in entityTypes)
            entities[type] = EntityModel.Read(type, diagnostics, entityTypes, compilation, schema);
        EntityModel? Lookup(INamedTypeSymbol type)
        {
            // An [Entity], or a [MapFrom] DTO as the rows of a hand-written statement (its properties the columns, no table).
            if (!entities.TryGetValue(type, out var model))
                entities[type] = model = EntityModel.IsEntity(type) ? EntityModel.Read(type, diagnostics, entityTypes, compilation, schema) : EntityModel.IsDeclaredDto(type) ? EntityModel.ForRaw(type, diagnostics) : null;
            return model;
        }

        // Every entity in reach (this assembly's and the referenced ones') by table name, for the hand-written statements' model check; read on the first statement.
        Dictionary<string, EntityModel>? byTable = null;
        EntityModel? ByTable(string table)
        {
            if (byTable is null)
            {
                byTable = new Dictionary<string, EntityModel>(StringComparer.OrdinalIgnoreCase);
                foreach (var type in AllTypes(compilation.GlobalNamespace).Where(EntityModel.IsEntity))
                {
                    if (Lookup(type) is { IsRawShape: false } model && !byTable.ContainsKey(model.Table))
                        byTable[model.Table] = model;
                }
            }

            return byTable.TryGetValue(table, out var found) ? found : null;
        }

        // Interceptors and plans first: translating them discovers the entities of referenced assemblies (the workload's) and their navigations' targets.
        var interceptors = new List<InterceptorModel>();
        var plans = new List<PlanSectionModel>(); // the terminals', then the [Query] definitions', then the hierarchy walks'
        var index = 0;
        foreach (var call in calls.Where(c => c is not null).Select(c => c!).OrderBy(c => c.Invocation.SyntaxTree.FilePath, StringComparer.Ordinal).ThenBy(c => c.Invocation.SpanStart))
        {
            if (call.Foreign)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.ChainTypedAsQueryable, call.Invocation.GetLocation(), call.Method.Name, call.Method.ContainingType.ToDisplayString()));
                continue;
            }

            if (InsideQueryDefinition(call.Invocation))
                continue; // a [Query] definition's chain is read as a whole, never executed: no interceptors for its call sites

            var entity = Lookup(call.Entity);
            if (entity is null)
                continue;
            foreach (var navigation in entity.Navigations)
                Lookup(navigation.Target);
            // The checksum form of InterceptsLocation (version, data), the one the compiler keeps; the path/line/column form is
            // deprecated (CS9270). Null when the call cannot be intercepted (a malformed invocation).
            var location = call.Model.GetInterceptableLocation(call.Invocation);
            if (location is null)
                continue;
            var attribute = location.GetInterceptsLocationAttributeSyntax();
            var emitted = IsFragment(call.Method)
                ? EmitFragmentCall(interceptors, call, entity, Lookup, index, attribute, diagnostics)
                : Terminals.Contains(call.Name)
                    ? EmitTerminal(interceptors, plans, call, entity, Lookup, ByTable, compilation, index, attribute, diagnostics, censusEntries)
                    : EmitOperator(interceptors, call, entity, Lookup, index, attribute, diagnostics);
            if (emitted)
                index++;
        }

        var compiledPredicates = new List<CompiledPredicateModel>();
        EmitSpecifications(interceptors, compiledPredicates, compilation, Lookup, diagnostics); // Pragmatic's specifications, compiled where they are written

        var declared = new List<DeclaredSectionModel>();
        var usings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in queries.OrderBy(q => q.ContainingType.ToDisplayString(), StringComparer.Ordinal).ThenBy(q => q.Name, StringComparer.Ordinal))
            EmitQuery(declared, usings, plans, query, Lookup, compilation, diagnostics, censusEntries);

        // The DTOs the runtime path may project into: their declared projections, registered with the maps.
        var projections = new List<(INamedTypeSymbol Dto, EntityModel Entity, Projection Projection, List<string> Joins)>();
        foreach (var dto in dtos.OrderBy(d => d.ToDisplayString(), StringComparer.Ordinal))
        {
            var attribute = dto.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "MapFromAttribute", TypeArguments.Length: 1 });
            var sourceEntity = attribute?.AttributeClass!.TypeArguments[0] as INamedTypeSymbol;
            var sourceModel = sourceEntity is null ? null : Lookup(sourceEntity);
            if (sourceModel is null)
                continue;
            var joins = new List<string>();
            var projection = Projection.TryDeclared(dto, sourceModel, Lookup, joins, diagnostics, dto.Locations.FirstOrDefault() ?? Location.None, compilation);
            if (projection is not null)
                projections.Add((dto, sourceModel, projection, joins));
        }

        // The maps describe navigations with their targets read at an offset: the targets of this assembly's entities are read here if a call site did not already.
        foreach (var entity in entities.Values.Where(e => e is not null).ToList())
        {
            foreach (var navigation in entity!.Navigations)
                Lookup(navigation.Target);
            foreach (var collection in entity.Collections)
                Lookup(collection.Child);
        }

        // The hierarchy walks of this assembly's entities: their plans with the others, their extensions in the entities' namespaces.
        var hierarchies = new List<TrailerSectionModel>();
        var own = entities.Values.Where(e => e is not null && !e.IsRawShape && SymbolEqualityComparer.Default.Equals(e.Type.ContainingAssembly, compilation.Assembly)).Select(e => e!).OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
        EmitHierarchies(plans, hierarchies, own, diagnostics);
        EmitTpcViews(hierarchies, own, Lookup, diagnostics); // the views of the table-per-concrete-type roots, as DDL text
        EmitTptViews(hierarchies, own, Lookup, diagnostics); // and of the table-per-type roots
        EmitSchemas(hierarchies, compilation, own, Lookup, diagnostics); // the [SqlDatabase] classes' schemas, for Pragmatic.Migrations
        if (EmitPragmaticSchema(compilation, Lookup, diagnostics) is { } pragmaticSchema) // and Pragmatic's entities', apart: it compiles alone
            spc.AddSource(pragmaticSchema.Hint, pragmaticSchema.Text);

        var models = entities.Values.Where(e => e is not null).Select(e => e!).OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
        var maps = ReadMaps(models, projections, functions, compilation, Lookup);
        var scopeRules = ReadScopeRules(models, compilation);
        EmitFunctionBodies(declared, functions);
        EmitCensus(spc, censusEntries, committedCensus, diagnostics);

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Location.SourceTree is { } tree && !input.Contains(tree))
            {
                // A file this generator added (a read repository, a query's shape): a problem is reported without a location, an error (a leaf
                // of a declared query that does not compile would otherwise surface only as a refusal at runtime); the notes are its own business.
                if (diagnostic.Severity >= DiagnosticSeverity.Warning)
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.InGeneratedSource, Location.None, tree.FilePath, diagnostic.Id, diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)));
                continue;
            }

            spc.ReportDiagnostic(census && Diagnostics.IsCensus(diagnostic) ? Diagnostics.AsCensus(diagnostic) : diagnostic);
        }
        spc.AddSource(new SqlSourceTemplate(maps, scopeRules, plans.ToEquatableArray(), compiledPredicates.ToEquatableArray(), interceptors.ToEquatableArray(), hierarchies.ToEquatableArray()).RenderOutput());
        if (declared.Count > 0)
            spc.AddSource(new SqlQueriesTemplate(usings.OrderBy(u => u, StringComparer.Ordinal), declared.ToEquatableArray()).RenderOutput());
    }

    internal static string Safe(EntityModel entity) => entity.FullName.Replace("global::", "").Replace('.', '_');

    private static bool InsideQueryDefinition(SyntaxNode node)
        => node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method && method.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() is "Query" or "QueryAttribute");

    /// <summary>Every named type under a namespace, nested namespaces included (the compilation's global namespace spans its references).</summary>
    internal static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) => PragmaticNames.AllTypes(ns);

    internal static bool TryParameter(SemanticModel model, LambdaExpressionSyntax lambda, out IParameterSymbol parameter)
    {
        parameter = null!;
        var symbol = model.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
        if (symbol is null || symbol.Parameters.Length != 1)
            return false;
        parameter = symbol.Parameters[0];
        return true;
    }

    /// <summary>The lambda's row parameter and, in the two-parameter form, its argument parameter; false for any other arity.</summary>
    internal static bool TryParameters(SemanticModel model, LambdaExpressionSyntax lambda, out IParameterSymbol parameter, out IParameterSymbol? arg)
    {
        parameter = null!;
        arg = null;
        var symbol = model.GetSymbolInfo(lambda).Symbol as IMethodSymbol;
        if (symbol is null || symbol.Parameters.Length is not (1 or 2))
            return false;
        parameter = symbol.Parameters[0];
        arg = symbol.Parameters.Length == 2 ? symbol.Parameters[1] : null;
        return true;
    }

    internal static ExpressionSyntax Body(LambdaExpressionSyntax lambda)
        => lambda.ExpressionBody ?? throw new InvalidOperationException("block lambdas are rejected before translation");

    internal static string Literal(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
