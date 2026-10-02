using Microsoft.CodeAnalysis;

namespace Pragmatic.Documents.Csv.Generator.Diagnostics;

/// <summary>Diagnostics for the CSV source generator (PRAG1900-1999 range — Documents).</summary>
internal static class CsvDiagnostics
{
    /// <summary>
    /// PRAG1900 — a property has a type the generated reader cannot parse back from text. It is written
    /// using ToString() but left at its default on read (write-only). The author should ignore it or use
    /// a supported type.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "PRAG1900",
        title: "CSV property type is not round-trippable",
        messageFormat: "Property '{0}.{1}' of type '{2}' is written as text but cannot be read back into its CLR type; " +
                       "it will be left at its default on read. Use a supported type or mark it [CsvColumn(Ignore = true)].",
        category: "Pragmatic.Documents.Csv",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
