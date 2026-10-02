using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Temporal.Diagnostics;

/// <summary>Diagnostic descriptors for the Temporal feature. Range: PRAG0900-PRAG0999 (0900-0904 live in Pragmatic.Temporal.Analyzers).</summary>
internal static class TemporalDiagnostics
{
    public static readonly DiagnosticDescriptor UnsupportedPropertyType = DiagnosticFactory.Warning(
        "PRAG0905", "Timezone conversion attribute on unsupported property type",
        "Property '{0}.{1}' has type '{2}' — timezone conversion attributes only apply to DateTimeOffset or DateTime (and their nullable forms); the attribute is ignored",
        "Apply timezone conversion attributes to DateTimeOffset/DateTime properties, or remove the attribute.");
}
