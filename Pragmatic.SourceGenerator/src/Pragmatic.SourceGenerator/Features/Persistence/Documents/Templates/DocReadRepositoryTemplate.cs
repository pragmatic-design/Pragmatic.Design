using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Templates;

/// <summary>
///     Pragmatic's <c>IReadRepository&lt;T&gt;</c> and <c>IRepository&lt;T&gt;</c> for a Pragmatic entity on the documents engine, nested in its partial
///     class (the Sql engine's <c>ReadRepositoryTemplate</c>, for documents), as source with chains on <c>DocQuery&lt;T&gt;</c>: the generator binds
///     and intercepts them like the application's own chains, so every read runs a compiled plan, the entity's filter families included.
/// </summary>
internal sealed class DocReadRepositoryTemplate(DocReadRepositoryModel model) : CSharpTemplate
{
    private const string Task = "global::System.Threading.Tasks.Task";
    private const string Token = "global::System.Threading.CancellationToken ct = default";
    private const string Spec = "global::Pragmatic.Specification.ISpecification";
    private const string ToPredicate = "global::Pragmatic.Persistence.Documents.Pragmatic.DocSpecifications.ToPredicate";

    protected override string? GeneratorName => "Pragmatic.Persistence.Documents";

    protected override string? SourceInfo => $"Pragmatic's read repository for {model.EntityName} on the documents engine";

    public override Artifact RenderOutput() => new(model.Hint, ToSourceText());

    public override void RenderFile()
    {
        var type = model.EntityType;
        var matching = $"Matching({ToPredicate}(spec))";
        if (model.Namespace is not null)
            AppendLine($"namespace {model.Namespace};");
        AppendLine();
        AppendLine($"partial class {model.EntityName}");
        AppendLine("{");
        AppendLine($"    /// <summary>Pragmatic's read repository for {model.EntityName} on the documents engine: every read a compiled plan, the session's filters applied.</summary>");
        AppendLine($"    public sealed class DocReadRepository(global::Pragmatic.Persistence.Documents.DocSession session) : global::Pragmatic.Persistence.Repository.IReadRepository<{type}>");
        AppendLine("    {");
        AppendLine($"        public async {Task}<{type}?> GetByIdAsync(global::System.Guid id, {Token})");
        AppendLine($"            => await session.Query<{type}>().FirstOrDefaultAsync((e, key) => e.PersistenceId == key, id, ct);");
        AppendLine();
        AppendLine($"        public async {Task}<global::System.Collections.Generic.List<{type}>> FindAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{matching}.ToListAsync(ct);");
        AppendLine();
        AppendLine($"        public async {Task}<int> CountAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => checked((int)await session.Query<{type}>().{matching}.CountAsync(ct));");
        AppendLine();
        AppendLine($"        public async {Task}<bool> ExistsAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{matching}.AnyAsync(ct);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> FirstOrDefaultAsync({Spec}<{type}> spec, {Token})");
        AppendLine($"            => await session.Query<{type}>().{matching}.FirstOrDefaultAsync(ct);");
        AppendLine();
        AppendLine($"        /// <summary>Not served by the documents engine: an IQueryable is EF Core's surface. Write the chain on session.Query&lt;{model.EntityName}&gt;(), or declare the read.</summary>");
        AppendLine($"        public global::System.Linq.IQueryable<{type}> Query()");
        AppendLine($"            => throw new global::System.NotSupportedException(\"The documents engine does not serve IReadRepository<{model.EntityName}>.Query(): an IQueryable is EF Core's surface. Write the chain on DocSession.Query<{model.EntityName}>(), or a specification.\");");
        AppendLine();
        RenderRunAsync(model.Queries, paged: false);
        RenderRunAsync(model.PagedQueries, paged: true);
        AppendLine("    }");
        RenderRepository();
        AppendLine("}");
    }

    /// <summary>One <c>RunAsync</c>: a case per declared query of the kind, a query the engine does not know refused.</summary>
    private void RenderRunAsync(EquatableArray<DocRunCaseModel> cases, bool paged)
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

        AppendLine($"            throw new global::System.NotSupportedException($\"{{query.GetType().Name}} is not a declared query of {model.EntityName} the documents engine compiled.\");");
        AppendLine("        }");
        AppendLine();
    }

    /// <summary>
    ///     One declared query's case, as the Sql engine's: its filters through Matching, its sorts through OrderedBy, its result projected (a Select
    ///     run in .NET, the document read with the fields it names), its page taken and counted.
    /// </summary>
    private void RenderCase(DocRunCaseModel query, bool paged)
    {
        if (query.Unsupported is not null)
        {
            AppendLine($"                case {query.QueryType}:");
            AppendLine($"                    throw new global::System.NotSupportedException({DocTypeEmitter.Literal($"{query.QueryName} is not compiled for the documents engine: {query.Unsupported}.")});");
            return;
        }

        var type = model.EntityType;
        AppendLine($"                case {query.QueryType} q:");
        AppendLine("                {");
        AppendLine($"                    var filtered = session.Query<{type}>().Matching({ToPredicate}(q.ToSpecification()));");
        if (paged)
        {
            AppendLine($"                    var paged = (global::Pragmatic.Persistence.Query.Interfaces.IPagedQuery<{type}>)q;");
            AppendLine($"                    var rows = await filtered.OrderedBy(q.ToDocOrdering()){query.Projection}.Skip(paged.Skip).Take(paged.Take).ToListAsync(ct);");
            AppendLine("                    var total = checked((int)await filtered.CountAsync(ct));");
            AppendLine($"                    return (global::Pragmatic.Persistence.Query.Results.PagedResult<TResult>)(object)global::Pragmatic.Persistence.Query.Results.PagedResult<{query.ResultType}>.Success(rows, total, paged.Page, paged.PageSize);");
        }
        else
        {
            AppendLine($"                    var rows = await filtered.OrderedBy(q.ToDocOrdering()){query.Projection}.ToListAsync(ct);");
            AppendLine("                    return (global::System.Collections.Generic.IReadOnlyList<TResult>)(object)rows;");
        }

        AppendLine("                }");
    }

    /// <summary>
    ///     Pragmatic's <c>IRepository&lt;T&gt;</c> over a <c>DocUnitOfWork</c>: the reads of the read repository, their documents tracked (the identity
    ///     map, the snapshot a save compares), the writes staged in the unit of work. A document staged without a read carries the check the save
    ///     runs first: the caller may write only a document it may read (the read by key, the session's filters applied).
    /// </summary>
    private void RenderRepository()
    {
        var type = model.EntityType;
        AppendLine();
        AppendLine($"    /// <summary>Pragmatic's repository for {model.EntityName} on the documents engine: the reads tracked by the unit of work, the writes staged in it.</summary>");
        AppendLine($"    public sealed class DocRepository(global::Pragmatic.Persistence.Documents.Pragmatic.DocUnitOfWork unitOfWork) : global::Pragmatic.Persistence.Repository.IRepository<{type}>");
        AppendLine("    {");
        AppendLine("        private readonly DocReadRepository _read = new(unitOfWork.Session);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> GetByIdAsync(global::System.Guid id, {Token})");
        AppendLine("            => await _read.GetByIdAsync(id, ct) is { } read ? unitOfWork.Attach(read) : null;");
        AppendLine();
        AppendLine($"        public async {Task}<global::System.Collections.Generic.List<{type}>> FindAsync({Spec}<{type}> spec, {Token})");
        AppendLine("            => unitOfWork.AttachAll(await _read.FindAsync(spec, ct));");
        AppendLine();
        AppendLine($"        public {Task}<int> CountAsync({Spec}<{type}> spec, {Token}) => _read.CountAsync(spec, ct);");
        AppendLine();
        AppendLine($"        public {Task}<bool> ExistsAsync({Spec}<{type}> spec, {Token}) => _read.ExistsAsync(spec, ct);");
        AppendLine();
        AppendLine($"        public async {Task}<{type}?> FirstOrDefaultAsync({Spec}<{type}> spec, {Token})");
        AppendLine("            => await _read.FirstOrDefaultAsync(spec, ct) is { } read ? unitOfWork.Attach(read) : null;");
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
        AppendLine("        /// <summary>The check a document staged without a read gets: the caller reads it by key, filtered.</summary>");
        AppendLine($"        private global::System.Func<global::System.Threading.CancellationToken, {Task}<bool>> Readable({type} entity)");
        AppendLine("        {");
        AppendLine("            var key = entity.PersistenceId;");
        AppendLine("            return async ct => await _read.GetByIdAsync(key, ct) is not null;");
        AppendLine("        }");
        AppendLine("    }");
    }
}
