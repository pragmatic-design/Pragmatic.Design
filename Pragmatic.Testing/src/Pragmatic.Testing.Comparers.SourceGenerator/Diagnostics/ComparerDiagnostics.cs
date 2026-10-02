using Microsoft.CodeAnalysis;

namespace Pragmatic.Testing.Comparers.SourceGenerator.Diagnostics;

/// <summary>
///     Diagnostics for <c>[GenerateComparer&lt;T&gt;]</c> (PRAG2360-2369, inside the Testing range
///     PRAG2350-2399).
/// </summary>
internal static class ComparerDiagnostics
{
    private const string Category = "Pragmatic.Testing";

    /// <summary>PRAG2360 — the type has nothing to compare.</summary>
    internal static readonly DiagnosticDescriptor NoComparableMembers = new(
        id: "PRAG2360",
        title: "[GenerateComparer<T>] needs readable public members",
        messageFormat: "'{0}' has no readable public property, so no comparer was generated. One "
                       + "would report every pair as equivalent, which is worse than not having it.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>PRAG2361 — the same type was declared twice.</summary>
    internal static readonly DiagnosticDescriptor DuplicateDeclaration = new(
        id: "PRAG2361",
        title: "Duplicate [GenerateComparer<T>] declaration",
        messageFormat: "'{0}' is declared more than once, and the extra declarations were ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
