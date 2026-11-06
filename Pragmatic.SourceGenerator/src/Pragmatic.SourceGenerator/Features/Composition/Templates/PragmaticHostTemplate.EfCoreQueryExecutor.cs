// Pragmatic.SourceGenerator - Composition - EF Core's query executor, for a host that references EF Core

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Partial: the query executor and the query hints of EF Core, registered by <c>RegisterAllDatabases</c> only when the host references
///     Pragmatic.Persistence.EFCore. A host whose databases the engines alone persist references no EF Core, and naming it there does not
///     compile (#552).
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    /// <summary>
    ///     EF Core's query executor and its hints, for a host that references Pragmatic.Persistence.EFCore. A host whose databases only the
    ///     engines persist references neither, and its boundaries' sessions run their own queries.
    /// </summary>
    private void RenderEfCoreQueryExecutor()
    {
        if (!_model.DetectedFeatures.HasPersistenceEFCore)
            return;

        // The fluent QueryBuilder's AsNoTracking()/AsSplitQuery() are EF Core concepts, and the
        // assembly that declares them does not reference EF Core. Installed here because this is
        // the path every EF host takes: AddPragmaticPersistenceEFCore exists and nobody calls it.
        Comment("Let the fluent builder's hints reach EF Core");
        AppendLine("global::Pragmatic.Persistence.EFCore.Query.EfQueryHintApplier.Install();");
        AppendLine();

        // Auto-register EfCoreQueryExecutor with full filter pipeline
        Comment("Auto-register EfCoreQueryExecutor (soft-delete, tenant, navigation filters, caching)");
        AppendLine("services.AddScoped<global::Pragmatic.Persistence.Query.Executors.IQueryExecutor>(sp =>");
        IncreaseIndent();
        AppendLine("new global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor(");
        IncreaseIndent();
        AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider>(),");
        AppendLine("sp.GetService<global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer>(),");
        AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle>(),");
        AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStack>(),");
        AppendLine("sp.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor>>(),");
        AppendLine("sp.GetService<global::Pragmatic.MultiTenancy.ITenantContext>(),");
        // Pass ICurrentUser so the query cache key includes the user discriminator
        // (permission/ownership filters) — without it cached rows leak across users.
        AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>(),");
        // The resolver routes a [Cacheable(Category = ...)] query to that category's stack.
        // Without it the executor falls back to the default one and a category's key prefix
        // and duration apply to nothing.
        AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStackResolver>(),");
        // The clock the FilterContext's Now comes from. Without it the executor falls back
        // to the wall clock and a test that pins time cannot pin a temporal filter.
        AppendLine("sp.GetService<global::System.TimeProvider>(),");
        // Where a [Join<T>(ForeignKey = …)] gets its target set: the root boundary's own
        // DbContext, through the keyed registration the boundary contexts already have.
        // ⚠️ This construction exists TWICE — here and in DbContextRegistrationTemplate — and a
        // host composed by PragmaticApp uses this one. Changing only the other copy leaves every
        // joined query answering 500 with the executor's own refusal: "built without an
        // IJoinSourceProvider".
        AppendLine("new global::Pragmatic.Persistence.EFCore.Query.BoundaryJoinSources(sp)));");
        DecreaseIndent();
        DecreaseIndent();
        AppendLine();
    }
}
