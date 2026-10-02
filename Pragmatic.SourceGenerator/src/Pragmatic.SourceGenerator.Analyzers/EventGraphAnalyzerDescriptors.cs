using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>Descriptors for the event-graph analyzer (#6b), in the Messaging range.</summary>
internal static class EventGraphAnalyzerDescriptors
{
    private const string Category = "Pragmatic.Design";

    // PRAG0822: a cycle in the event → handler → operation → event graph (risk of an unbounded cascade).
    // (0820 is owned by MessagingDiagnostics.SagaEventWithoutCorrelation — must not collide.)
    public static readonly DiagnosticDescriptor Prag0822 = new(
        "PRAG0822",
        "Domain-event cascade cycle",
        "Domain events form a cascade cycle: {0}. A handler reacting to one of these events triggers an " +
        "operation that re-raises another, looping back — risking an unbounded cascade. Break the loop " +
        "(make a handler idempotent/terminal or guard the re-raise).",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);
}
