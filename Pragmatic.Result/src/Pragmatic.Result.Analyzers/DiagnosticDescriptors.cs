using Microsoft.CodeAnalysis;

namespace Pragmatic.Result.Analyzers;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor UnsafeValueAccess = new(
        id: "PRAG0001",
        title: "Unsafe Result.Value access",
        messageFormat: "Accessing '{0}.Value' without checking 'IsSuccess' may throw InvalidOperationException",
        category: "Pragmatic.Result",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Always check IsSuccess or IsFailure before accessing .Value on a Result type. Use pattern matching, Match(), or TryGetValue() for safe access.");

    public static readonly DiagnosticDescriptor MissingPartialOnErrorWithCustomProperties = new(
        id: "PRAG0002",
        title: "Missing 'partial' on error type with custom properties",
        messageFormat: "Error type '{0}' declares custom properties but is not 'partial'; the source generator cannot emit WriteExtensions, so these properties will not appear in ProblemDetails. Add the 'partial' modifier.",
        category: "Pragmatic.Result",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The source generator emits the WriteExtensions override — which surfaces an error type's custom properties in ProblemDetails and OpenAPI — only for types declared 'partial'. A non-partial error that adds custom properties compiles without warning, but those properties are silently dropped from the HTTP response. Fix by adding the 'partial' modifier to the type declaration so the generator can contribute the override, or by writing a manual override of WriteExtensions to emit the properties yourself.");
}
