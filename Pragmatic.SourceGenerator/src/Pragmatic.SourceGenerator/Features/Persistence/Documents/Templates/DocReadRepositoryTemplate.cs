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
    private const string ToPredicate = "global::Pragmatic.Persistence.Documents.DocSpecifications.ToPredicate";

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
        AppendLine($"    public sealed class DocRepository(global::Pragmatic.Persistence.Documents.DocUnitOfWork unitOfWork) : global::Pragmatic.Persistence.Repository.IRepository<{type}>,");
        AppendLine($"        global::Pragmatic.Persistence.Repository.IBulkRepository<{type}>, global::Pragmatic.Persistence.Documents.IDocRepository");
        AppendLine("    {");
        AppendLine("        private readonly DocReadRepository _read = new(unitOfWork.Session);");
        AppendLine();
        AppendLine("        /// <summary>The unit of work this repository reads and writes through.</summary>");
        AppendLine("        public global::Pragmatic.Persistence.Documents.DocUnitOfWork UnitOfWork => unitOfWork;");
        AppendLine();
        RenderBulk(type);
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

    /// <summary>
    ///     <c>IBulkRepository</c>: the update is compiled where it is called (the generator intercepts the call, its plan run by <c>DocBulk</c>), so
    ///     this body runs only for a call the generator did not see, and says so. The delete is a chain on the session, compiled here: the
    ///     documents the specification selects among the caller's, marked when the entity is <c>[SoftDelete]</c> (the instant and the caller, as
    ///     the unit of work marks one), deleted inside <c>SoftDeleteScope.Suspend</c> or when it is not. The insert and the upsert are the unit of
    ///     work's, the save's phases applied to the documents alone; the upsert replaces only a document the caller may read.
    /// </summary>
    private void RenderBulk(string type)
    {
        const string NotCompiled = "throw new global::System.NotSupportedException(\"BulkUpdateAsync on the documents engine is compiled where it is called: this call was not seen by the generator (written in another assembly, or reached by reflection). Call it on IBulkRepository<{0}> in an assembly the Pragmatic generator runs on.\");";
        AppendLine($"        public {Task}<int> BulkUpdateAsync({Spec}<{type}> filter, global::System.Action<global::Pragmatic.Persistence.Repository.IBulkSetters<{type}>> set, {Token})");
        AppendLine("            => " + string.Format(System.Globalization.CultureInfo.InvariantCulture, NotCompiled, model.EntityName));
        AppendLine();
        AppendLine($"        public async {Task}<int> BulkDeleteAsync({Spec}<{type}> filter, {Token})");
        AppendLine("        {");
        AppendLine($"            var documents = unitOfWork.Session.Query<{type}>().Matching({ToPredicate}(filter));");
        if (model.SoftDelete)
        {
            AppendLine("            if (!global::Pragmatic.Persistence.Entity.SoftDeleteScope.IsSuspended)");
            AppendLine("                return checked((int)await documents.ExecuteUpdateAsync(static (s, stamp) => s.Set(o => o.IsDeleted, true).Set(o => o.DeletedAt, stamp.At).Set(o => o.DeletedBy, stamp.By), unitOfWork.DeletionStamp(), ct));");
        }

        AppendLine("            return checked((int)await documents.ExecuteDeleteAsync(ct));");
        AppendLine("        }");
        AppendLine();
        AppendLine($"        public {Task}<int> BulkInsertAsync(global::System.Collections.Generic.IReadOnlyList<{type}> entities, global::System.Threading.CancellationToken ct)");
        AppendLine("            => unitOfWork.InsertManyAsync(entities, ct);");
        AppendLine();
        AppendLine($"        public {Task}<int> BulkUpsertAsync(global::System.Collections.Generic.IReadOnlyList<{type}> entities, global::System.Threading.CancellationToken ct)");
        AppendLine("            => unitOfWork.UpsertManyAsync(entities, async (entity, token) => await _read.GetByIdAsync(entity.PersistenceId, token) is not null, ct);");
        AppendLine();
    }
}
