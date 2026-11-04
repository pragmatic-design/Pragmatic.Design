using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Actions.Diagnostics;

/// <summary>
///     A package whose rows live in the importing boundary's database (<c>Core.PackageStores</c>): the store its operations read
///     through is the one of that boundary's engine.
/// </summary>
internal static partial class ActionsDiagnostics
{
    /// <summary>PRAG0469: the package is imported into a boundary whose engine's store the compilation does not reference.</summary>
    /// <remarks>
    ///     The package names no engine, so a module on any of them can import it; the store of the importing boundary's engine is in a
    ///     package of its own, and without it the package's operations would ask the container for a store nothing registered. An error
    ///     at the import, naming the package to add, rather than an application that fails its container validation at start.
    /// </remarks>
    public static readonly DiagnosticDescriptor PackageStoreNotReferenced = new(
        "PRAG0469",
        "The imported package's store is not referenced for the boundary's engine",
        "'{0}' is imported into '{1}', which is persisted by {2}: reference '{3}', which stores the package's rows on that engine",
        Category, DiagnosticSeverity.Error, true);
}
