using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     Diagnostic descriptors for <c>[ReadAccess&lt;TEntity&gt;]</c> boundary declarations.
///     Range: PRAG0706.
/// </summary>
internal static class ReadAccessDiagnostics
{
    /// <summary>
    ///     A boundary reads an entity that the host topology places on a DIFFERENT database.
    ///     <c>[ReadAccess]</c> is a SQL-join mechanism: the entity is added as a <c>DbSet</c> to the
    ///     reading boundary's DbContext (with <c>ExcludeFromMigrations</c>, so its table is created only in
    ///     the OWNING database). The generated code compiles and then fails at runtime on the first query,
    ///     against a table the connection cannot see.
    ///     <para>
    ///     Reported by <see cref="Validation.ReadAccessTopologyValidator"/>.
    ///     </para>
    /// </summary>
    public static readonly DiagnosticDescriptor ReadAccessCrossDatabase = DiagnosticFactory.Warning(
        "PRAG0706",
        "[ReadAccess] target lives on another database",
        "Boundary '{0}' declares [ReadAccess<{1}>], but '{0}' is mapped to database '{2}' while '{1}' is owned " +
        "by boundary '{3}' on database '{4}'. [ReadAccess] adds '{1}' as a DbSet on the '{0}' DbContext and " +
        "excludes it from that database's migrations, so the table exists only in '{4}': the generated join " +
        "compiles and fails at runtime against a missing table",
        "[ReadAccess] is a same-database SQL join. Either map both modules to the same database in the host " +
        "[Include<TModule, TDatabase>] declarations, or drop the [ReadAccess] and cross the boundary with a " +
        "query/action call, a published read contract, or replicated data.");
}
