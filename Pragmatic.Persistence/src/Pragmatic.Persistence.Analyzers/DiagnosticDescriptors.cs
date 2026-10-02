using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Analyzers;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor PreferEntityCreate = new(
        id: "PRAG0680",
        title: "Use Entity.Create() factory instead of new",
        messageFormat: "Entity '{0}' should be created via '{0}.Create()' factory method instead of 'new {0}()'",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Entities decorated with [Entity] generate a static Create() factory method that ensures proper ID generation (Guid v7), audit timestamps, and default values. Using 'new' directly bypasses these initializations.");

    public static readonly DiagnosticDescriptor DoNotDefaultEntity = new(
        id: "PRAG0681",
        title: "Do not use default for entity types",
        messageFormat: "Entity '{0}' must not be created via 'default' — use '{0}.Create()' factory method instead",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using 'default' or 'default(Entity)' creates an uninitialized entity instance that bypasses ID generation, audit timestamps, and default values. Use the generated Create() factory method instead.");

    public static readonly DiagnosticDescriptor DoNotReflectEntity = new(
        id: "PRAG0682",
        title: "Do not use Activator.CreateInstance for entity types",
        messageFormat: "Entity '{0}' must not be created via reflection — use '{0}.Create()' factory method instead",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Using Activator.CreateInstance to create entities bypasses ID generation, audit timestamps, and default values. Use the generated Create() factory method instead. Pragmatic follows a zero-reflection principle.");

    public static readonly DiagnosticDescriptor RawSqlInjection = new(
        id: "PRAG0684",
        title: "Avoid non-constant SQL in FromSqlRaw/ExecuteSqlRaw",
        messageFormat: "'{0}' is called with non-constant SQL — interpolating or concatenating runtime values opens a SQL injection hole; use FromSql/ExecuteSql (interpolated, auto-parameterized) or pass values via the parameters argument",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The *Raw EF Core APIs (FromSqlRaw, ExecuteSqlRaw, SqlQueryRaw) do NOT parameterize their SQL string. Passing an interpolated or concatenated string built from runtime values allows SQL injection. Use FromSql/FromSqlInterpolated/ExecuteSql, which capture an interpolated string and parameterize every hole, or pass user values through the 'parameters' argument. A constant SQL literal (or interpolation with only constant holes) is safe and is not flagged.");

    public static readonly DiagnosticDescriptor CrossBoundaryDbContext = new(
        id: "PRAG0686",
        title: "Cross-boundary DbContext access (reach-in)",
        messageFormat: "'{0}' injects '{1}', the DbContext of the '{2}' boundary, from outside that boundary. Reaching into another boundary's persistence couples their schemas and breaks module independence — call that boundary's actions/queries, or react to its integration events, instead.",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Each boundary owns its DbContext and schema. A type from another boundary that injects that DbContext bypasses the boundary's public surface (actions/queries/events), couples directly to its tables, and turns the modular monolith into a distributed-ball-of-mud that cannot be extracted later. The owning boundary is taken from the generated [PragmaticDbContext(\"<boundary>\")] attribute; a consumer whose namespace does not belong to that boundary is flagged. Use the boundary's generated actions/queries or subscribe to its events.");

    // Disabled by default: a heuristic DDD smell, not a hard rule. Enable via .editorconfig for teams
    // that want aggregate-size pressure surfaced. Threshold is fixed at 5 owned child collections.
    public static readonly DiagnosticDescriptor AggregateTooLarge = new(
        id: "PRAG0685",
        title: "Aggregate has many child collections — consider splitting",
        messageFormat: "Entity '{0}' owns {1} child collections. Large aggregates increase write contention, memory and latency — consider splitting the aggregate or referencing other aggregates by id instead of containing them.",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        description: "DDD guidance: keep aggregates small. Every owned child collection widens the consistency boundary that a single transaction must lock, raising contention and the cost of loading/saving the aggregate. When an entity accumulates many child collections, prefer splitting it into smaller aggregates that reference each other by id (and coordinate via domain events) rather than containing everything. Counts collection-typed navigations whose element is itself an [Entity].");

    /// <summary>PRAG0687: a bulk delete on a soft-delete entity, which no interceptor can see.</summary>
    /// <remarks>
    ///     Soft delete is enforced at save time, which is what makes it hold on every path that reaches
    ///     the change tracker — a repository call, a mutation, a child leaving its parent's collection,
    ///     a hand-written <c>Remove</c>. <c>ExecuteDelete</c> reaches none of it: it is translated
    ///     straight to SQL. So this is the one way left to permanently delete a row the framework
    ///     promised was recoverable, and the only place it can be caught is here.
    /// </remarks>
    public static readonly DiagnosticDescriptor BulkDeleteOnSoftDeleteEntity = new(
        id: "PRAG0687",
        title: "Bulk delete permanently removes a soft-delete entity",
        messageFormat:
        "'{0}' is [SoftDelete], and ExecuteDelete removes its rows for good. The statement goes straight to SQL without passing the change tracker, so nothing converts it into the flag. Use the repository, or say the erasure is meant with SoftDeleteScope.Suspend().",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Every other delete path converges on SaveChanges, where the soft-delete interceptor turns it into IsDeleted. ExecuteDelete and ExecuteDeleteAsync are translated to a DELETE statement and never enter the change tracker, so the guarantee the entity's [SoftDelete] declares does not hold for them. That is legitimate when erasure is the intent — a subject's request under GDPR has to remove the row — and SoftDeleteScope.Suspend() is how that intent is stated. Without it, the rows are gone and nothing recorded that they were.");

    /// <summary>PRAG0688: a bulk update on an entity whose declared behaviour lives in SaveChanges.</summary>
    /// <remarks>
    ///     The twin of PRAG0687 on the operation that touches the most rows. ExecuteUpdate is
    ///     translated straight to SQL and never enters the change tracker, so the audit columns stay
    ///     where they were and the concurrency token does not move — a client holding a stale copy
    ///     will save over the change believing it fresh. The query filters do survive; it is the write
    ///     half that is skipped.
    /// </remarks>
    public static readonly DiagnosticDescriptor BulkUpdateSkipsInterceptors = new(
        id: "PRAG0688",
        title: "Bulk update skips the interceptors the entity declares",
        messageFormat:
        "'{0}' declares {1}, which is applied at SaveChanges. ExecuteUpdate goes straight to SQL "
        + "without passing the change tracker, so the audit columns are not stamped and the "
        + "concurrency token does not move. Use the repository, or accept the trade deliberately.",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
        "Every other write path converges on SaveChanges, where the interceptors stamp the audit "
        + "columns and bump the concurrency token. ExecuteUpdate and ExecuteUpdateAsync are "
        + "translated to an UPDATE statement and never enter the change tracker, so those guarantees "
        + "do not hold for them — silently, and on the operation that by definition touches the most "
        + "rows. The tenant and soft-delete filters are unaffected: the rows are selected through a "
        + "queryable that carries them.");

    // Disabled by default: a codebase migrates its entities to the anemic model before turning this on
    // (otherwise it floods existing behavior-rich entities). Enable via .editorconfig once migrated.
    public static readonly DiagnosticDescriptor EntityShouldHaveNoBehavior = new(
        id: "PRAG0683",
        title: "Entity should not declare behavior methods",
        messageFormat: "Entity '{0}' declares the behavior method '{1}'. In the Pragmatic model entities are anemic — move the state change into a mutation or domain action.",
        category: "Pragmatic.Persistence",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: false,
        description: "Entities are anemic in Pragmatic: data plus declarative invariants (validation, state machine, relations) and computed projections. Behavior — anything that changes state — belongs in a mutation or domain action, where the generated invoker governs persistence, events and authorization. User-declared methods on an [Entity] type signal behavior leaking into the entity. Excludes generated plumbing, computed getters, operators and Deconstruct.");
}
