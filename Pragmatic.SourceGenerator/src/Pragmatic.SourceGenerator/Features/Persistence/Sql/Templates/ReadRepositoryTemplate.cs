using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Templates;

/// <summary>
///     Pragmatic's <c>IReadRepository&lt;T&gt;</c> and <c>IRepository&lt;T&gt;</c> for a Pragmatic entity on the Sql engine, nested in its partial
///     class, as source with chains on <c>SqlQuery&lt;T&gt;</c>: the generator binds and intercepts them like the application's own chains.
/// </summary>
internal sealed class ReadRepositoryTemplate(ReadRepositoryModel model) : CSharpTemplate
{
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Token = "global::System.Threading.CancellationToken ct = default";
    private const string Spec = "global::Pragmatic.Specification.ISpecification";
    private const string Matching = "Matching(global::Pragmatic.Persistence.Sql.SqlSpecifications.ToPredicate(spec))";
    private const string Including = "global::Pragmatic.Persistence.Repository.IIncludingReadRepository";

    protected override string? GeneratorName => "Pragmatic.Persistence.Sql";

    protected override string? SourceInfo => $"Pragmatic's read repository for {model.EntityName} on the Sql engine";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var type = model.EntityType;
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.EntityName}");
        AppendLine("{");
        AppendLine($"    /// <summary>Pragmatic's read repository for {model.EntityName} on the Sql engine: every read a compiled plan, the session's filters applied.</summary>");
        AppendLine($"    public sealed class SqlReadRepository(global::Pragmatic.Persistence.Sql.SqlSession session) : global::Pragmatic.Persistence.Repository.IReadRepository<{type}>, {Including}<{type}>");
        AppendLine("    {");
        AppendLine($"        public async {Task}<{type}?> GetByIdAsync(global::System.Guid id, {Token})");
        AppendLine($"            => await session.Query<{type}>().FirstOrDefaultAsync((e, key) => e.PersistenceId == key, id, ct);");
        AppendLine();
        RenderGetByIdIncluding();
        AppendLine($"        public async {Task}<global::System.Collections.Generic.List<{type}>> FindAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{Matching}.ToListAsync(ct);");
        AppendLine();
        AppendLine($"        public async {Task}<int> CountAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{Matching}.CountAsync(ct);");
        AppendLine();
        AppendLine($"        public async {Task}<bool> ExistsAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{Matching}.AnyAsync(ct);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> FirstOrDefaultAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{Matching}.FirstOrDefaultAsync(ct);");
        AppendLine();
        AppendLine($"        /// <summary>Not served by the Sql engine: an IQueryable is EF Core's surface. Write the chain on session.Query&lt;{model.EntityName}&gt;(), or declare the read.</summary>");
        AppendLine($"        public global::System.Linq.IQueryable<{type}> Query()");
        AppendLine($"            => throw new global::System.NotSupportedException(\"The Sql engine does not serve IReadRepository<{model.EntityName}>.Query(): an IQueryable is EF Core's surface. Write the chain on SqlSession.Query<{model.EntityName}>(), or declare the read ([Query], a specification).\");");
        AppendLine();
        RenderRunAsync(model.Queries, paged: false);
        RenderRunAsync(model.PagedQueries, paged: true);
        AppendLine("    }");
        RenderRepository();
        AppendLine("}");
    }

    /// <summary>
    ///     <c>GetByIdIncludingAsync</c>: a case per set of include paths the compilation's loads declare, each a chain with its <c>Include</c>s
    ///     the generator binds and intercepts; a key no load declared refused.
    /// </summary>
    private void RenderGetByIdIncluding()
    {
        var type = model.EntityType;
        AppendLine($"        public async {Task}<{type}?> GetByIdIncludingAsync(global::System.Guid id, string includes, {Token})");
        AppendLine("        {");
        if (model.IncludeReads.Count > 0)
        {
            AppendLine("            switch (includes)");
            AppendLine("            {");
            foreach (var read in model.IncludeReads)
            {
                AppendLine($"                case {SqlInterceptorGenerator.Literal(read.Key)}:");
                AppendLine($"                    return await session.Query<{type}>(){string.Concat(read.Paths.Select(p => $".Include({Lambda(p)})"))}.FirstOrDefaultAsync((e, key) => e.PersistenceId == key, id, ct);");
            }

            AppendLine("            }");
            AppendLine();
        }
        else
        {
            AppendLine($"            await {Task}.CompletedTask; // no load of this entity declares an Include: every call is refused below");
        }

        AppendLine($"            throw new global::System.NotSupportedException($\"No load of {model.EntityName} the Sql engine compiled ([LoadEntity] or a mutation's) includes '{{includes}}'.\");");
        AppendLine("        }");
        AppendLine();
    }

    /// <summary>A path's lambda: its first navigation, then each next one below it with <c>Then</c> (<c>e => e.RoomTypes.Then(n1 => n1.Rates)</c>).</summary>
    private static string Lambda(string path)
    {
        var segments = path.Split('.');
        var body = segments[segments.Length - 1];
        for (var i = segments.Length - 1; i > 0; i--)
            body = $"{segments[i - 1]}.Then(n{i} => n{i}.{body})";
        return $"e => e.{body}";
    }

    /// <summary>One <c>RunAsync</c>: a case per declared query of the kind, a query the engine does not know refused.</summary>
    private void RenderRunAsync(EquatableArray<RunCaseModel> cases, bool paged)
    {
        var result = paged ? "global::Pragmatic.Persistence.Query.Results.PagedResult<TResult>" : "global::System.Collections.Generic.IReadOnlyList<TResult>";
        AppendLine($"        public async {Task}<{result}> RunAsync<TResult>(global::Pragmatic.Persistence.Query.Interfaces.{(paged ? "IPagedQuery" : "IQuery")}<{model.EntityType}, TResult> query, {Token}) where TResult : class");
        AppendLine("        {");
        if (cases.Count > 0)
        {
            AppendLine("            switch (query)");
            AppendLine("            {");
            foreach (var query in cases)
                RenderCase(query, paged);
            AppendLine("            }");
            AppendLine();
        }
        else
        {
            AppendLine($"            await {Task}.CompletedTask; // no declared query of this kind: every call is refused below");
        }

        AppendLine($"            throw new global::System.NotSupportedException($\"{{query.GetType().Name}} is not a declared query of {model.EntityName} the Sql engine compiled.\");");
        AppendLine("        }");
        AppendLine();
    }

    /// <summary>One declared query's case: its filters through Matching, its sorts through OrderedBy, its result projected, its page taken and counted.</summary>
    private void RenderCase(RunCaseModel query, bool paged)
    {
        if (query.Unsupported is not null)
        {
            AppendLine($"                case {query.QueryType}:");
            AppendLine($"                    throw new global::System.NotSupportedException({SqlInterceptorGenerator.Literal($"{query.QueryName} is not compiled for the Sql engine: {query.Unsupported}.")});");
            return;
        }

        var type = model.EntityType;
        var project = query.Projected ? $".Project<{query.ResultType}>()" : "";
        var filtered = $"session.Query<{type}>().Matching(global::Pragmatic.Persistence.Sql.SqlSpecifications.ToPredicate(q.ToSpecification()))";
        AppendLine($"                case {query.QueryType} q:");
        AppendLine("                {");
        if (paged)
        {
            AppendLine($"                    var paged = (global::Pragmatic.Persistence.Query.Interfaces.IPagedQuery<{type}>)q;");
            AppendLine($"                    var rows = await {filtered}.OrderedBy(q.ToSqlOrdering()){project}.Skip(paged.Skip).Take(paged.Take).ToListAsync(ct);");
            AppendLine($"                    var total = await {filtered}.CountAsync(ct);");
            AppendLine($"                    return (global::Pragmatic.Persistence.Query.Results.PagedResult<TResult>)(object)global::Pragmatic.Persistence.Query.Results.PagedResult<{query.ResultType}>.Success(rows, total, paged.Page, paged.PageSize);");
        }
        else
        {
            AppendLine($"                    var rows = await {filtered}.OrderedBy(q.ToSqlOrdering()){project}.ToListAsync(ct);");
            AppendLine("                    return (global::System.Collections.Generic.IReadOnlyList<TResult>)(object)rows;");
        }

        AppendLine("                }");
    }

    /// <summary>
    ///     Pragmatic's <c>IRepository&lt;T&gt;</c> over a unit of work: the reads of the read repository, their rows tracked (the identity map,
    ///     the snapshot a save compares), the writes staged in the unit of work. A row staged without a read carries the check the save runs
    ///     first: the caller may write only a row it may read (the read by key, the session's filters applied). <c>RunAsync</c>'s rows are
    ///     not tracked: a declared query's result is read, not loaded for a change.
    /// </summary>
    private void RenderRepository()
    {
        var type = model.EntityType;
        AppendLine();
        AppendLine($"    /// <summary>Pragmatic's repository for {model.EntityName} on the Sql engine: the reads tracked by the unit of work, the writes staged in it.</summary>");
        AppendLine($"    public sealed class SqlRepository(global::Pragmatic.Persistence.Sql.SqlUnitOfWork unitOfWork) : global::Pragmatic.Persistence.Repository.IRepository<{type}>, {Including}<{type}>,");
        AppendLine($"        global::Pragmatic.Persistence.Repository.INavigationLoader<{type}>");
        AppendLine("    {");
        AppendLine("        private readonly SqlReadRepository _read = new(unitOfWork.Session);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> GetByIdAsync(global::System.Guid id, {Token})");
        AppendLine("            => await _read.GetByIdAsync(id, ct) is { } row ? unitOfWork.Attach(row) : null;");
        AppendLine();
        // The unit of work tracks the root: the included children are read, and a change to them is not saved by this read.
        AppendLine($"        public async {Task}<{type}?> GetByIdIncludingAsync(global::System.Guid id, string includes, {Token})");
        AppendLine("            => await _read.GetByIdIncludingAsync(id, includes, ct) is { } row ? unitOfWork.Attach(row) : null;");
        AppendLine();
        AppendLine($"        public async {Task}<global::System.Collections.Generic.List<{type}>> FindAsync({Spec}<{type}> spec, {Token})");
        AppendLine("            => unitOfWork.AttachAll(await _read.FindAsync(spec, ct));");
        AppendLine();
        // A handed-over entity's graph: the read of its row with these paths (a compiled read of the set, as GetByIdIncludingAsync's), merged
        // into it. Not EF Core's planner of the missing paths: the paths are the ones the mutation's invoker compiled its read for.
        AppendLine("        /// <summary>Reads the entity's row with <paramref name=\"paths\" /> and merges the graph into it: 0 with no paths, 1 otherwise.</summary>");
        AppendLine($"        public async {Task}<int> EnsureLoadedAsync({type} entity, global::System.Collections.Generic.IReadOnlyList<string> paths, global::System.Threading.CancellationToken ct)");
        AppendLine("        {");
        AppendLine("            if (paths.Count == 0)");
        AppendLine("                return 0;");
        AppendLine("            if (await _read.GetByIdIncludingAsync(entity.PersistenceId, string.Join(\",\", paths), ct) is { } read)");
        AppendLine("                unitOfWork.LoadGraph(entity, read);");
        AppendLine("            return 1;");
        AppendLine("        }");
        AppendLine();
        AppendLine($"        public {Task}<int> CountAsync({Spec}<{type}> spec, {Token}) => _read.CountAsync(spec, ct);");
        AppendLine();
        AppendLine($"        public {Task}<bool> ExistsAsync({Spec}<{type}> spec, {Token}) => _read.ExistsAsync(spec, ct);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> FirstOrDefaultAsync({Spec}<{type}> spec, {Token})");
        AppendLine("            => await _read.FirstOrDefaultAsync(spec, ct) is { } row ? unitOfWork.Attach(row) : null;");
        AppendLine();
        AppendLine($"        public global::System.Linq.IQueryable<{type}> Query() => _read.Query();");
        AppendLine();
        AppendLine($"        public {Task}<global::System.Collections.Generic.IReadOnlyList<TResult>> RunAsync<TResult>(global::Pragmatic.Persistence.Query.Interfaces.IQuery<{type}, TResult> query, {Token}) where TResult : class => _read.RunAsync(query, ct);");
        AppendLine();
        AppendLine($"        public {Task}<global::Pragmatic.Persistence.Query.Results.PagedResult<TResult>> RunAsync<TResult>(global::Pragmatic.Persistence.Query.Interfaces.IPagedQuery<{type}, TResult> query, {Token}) where TResult : class => _read.RunAsync(query, ct);");
        AppendLine();
        AppendLine($"        public void Add({type} entity) => unitOfWork.Add(entity);");
        AppendLine();
        AppendLine($"        public void AddRange(global::System.Collections.Generic.IEnumerable<{type}> entities) {{ foreach (var entity in entities) unitOfWork.Add(entity); }}");
        AppendLine();
        AppendLine($"        public void Update({type} entity) => unitOfWork.Update(entity, Readable(entity));");
        AppendLine();
        AppendLine($"        public void Remove({type} entity) => unitOfWork.Remove(entity, Readable(entity));");
        AppendLine();
        AppendLine($"        public void RemoveRange(global::System.Collections.Generic.IEnumerable<{type}> entities) {{ foreach (var entity in entities) unitOfWork.Remove(entity, Readable(entity)); }}");
        AppendLine();
        AppendLine("        /// <summary>The check a row staged without a read gets: the caller reads it by key, filtered.</summary>");
        AppendLine($"        private global::System.Func<global::System.Threading.CancellationToken, {Task}<bool>> Readable({type} entity)");
        AppendLine("        {");
        AppendLine("            var key = entity.PersistenceId;");
        AppendLine("            return async ct => await _read.GetByIdAsync(key, ct) is not null;");
        AppendLine("        }");
        AppendLine("    }");
    }
}
