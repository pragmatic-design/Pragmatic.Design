using Microsoft.CodeAnalysis;

namespace Pragmatic.Ensure.Analyzers;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor HandWrittenArgumentGuard = new(
        id: "PRAG0100",
        title: "Hand-written argument guard where Ensure applies",
        messageFormat: "Guard on '{0}' throws {1} by hand; use Ensure.{2} instead",
        category: "Pragmatic.Ensure",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Ensure is the guard vocabulary of the framework, and a rule nothing enforces is a "
            + "convention: before this analyzer existed the repository held 76 hand-written argument "
            + "guards across 52 files while the coding standard said to use Ensure. Beyond consistency, "
            + "Ensure carries the caller-argument expression so the message names the real parameter, "
            + "and it is aggressively inlined so the guarded path costs nothing. Info rather than "
            + "Warning: this is a house style, not a defect, and a build that fails on style is a build "
            + "people learn to bypass.");
}
