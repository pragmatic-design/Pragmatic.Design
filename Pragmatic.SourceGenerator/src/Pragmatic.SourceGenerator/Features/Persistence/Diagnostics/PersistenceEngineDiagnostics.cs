using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0743</c>-<c>PRAG0744</c>: the persistence engine of an entity, which its boundary names with
///     <c>[PersistenceEngine]</c>.
/// </summary>
/// <remarks>
///     Both are errors: an entity whose engine cannot be decided is persisted by none, which is an absence rather than a
///     failure — no repository, no table, and the first write is what says so.
/// </remarks>
internal static class PersistenceEngineDiagnostics
{
    /// <summary>PRAG0743: two or more engines referenced, and the entity's boundary names none.</summary>
    public static readonly DiagnosticDescriptor EntityHasNoPersistenceEngine = DiagnosticFactory.Error(
        "PRAG0743",
        "The entity's boundary names no persistence engine",
        "'{0}' has no persistence engine: its boundary '{1}' names none, and this compilation references {2}",
        "Add [PersistenceEngine(PersistenceEngineKind.EfCore)] (or Sql, or Documents) to the boundary. With one engine referenced it is the "
        + "default; with several, each boundary says which one persists its entities.");

    /// <summary>PRAG0744: the boundary names an engine the compilation does not reference.</summary>
    public static readonly DiagnosticDescriptor PersistenceEngineNotReferenced = DiagnosticFactory.Error(
        "PRAG0744",
        "The boundary names a persistence engine that is not referenced",
        "'{0}' names the engine {1}, which this compilation does not reference (it references {2})",
        "Reference the engine's package, or name one the compilation references.");
}
