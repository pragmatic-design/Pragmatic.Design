using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.ValueObject.Diagnostics;

internal static class ValueObjectDiagnostics
{
    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "PRAG2700",
        title: "[ValueObject] type must be partial",
        messageFormat: "Value object '{0}' must be declared 'partial' so Create/CreateUnsafe can be generated",
        category: "Pragmatic.ValueObject",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingValidate = new(
        id: "PRAG2701",
        title: "[ValueObject] type missing Validate method",
        messageFormat: "Value object '{0}' must declare a 'private static' Validate method; Create cannot be generated without it",
        category: "Pragmatic.ValueObject",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // NOTE: PRAG2702 (NotRecord) was declared here but never reported — ValueObjectFeature never checks
    // whether the type is a record. Removed: do not reuse the ID for anything else.
}
