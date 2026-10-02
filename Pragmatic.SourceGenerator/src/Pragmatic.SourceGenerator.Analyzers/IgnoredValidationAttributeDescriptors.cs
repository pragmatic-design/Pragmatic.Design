using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Descriptor for <c>PRAG0210</c>, reported when a property carries a validation attribute the
///     Validation generator will not act on.
/// </summary>
internal static class IgnoredValidationAttributeDescriptors
{
    public static readonly DiagnosticDescriptor Prag0210 = new(
        "PRAG0210",
        "Validation attribute is ignored by the generator",
        "'{0}' is not a Pragmatic validation attribute and generates no check{1}",
        "Pragmatic.Validation",
        DiagnosticSeverity.Warning,
        true,
        "The Validation generator acts on attributes from Pragmatic.Validation.Attributes, or on types " +
        "deriving from its ValidationAttribute. Anything else is dropped, and the value reaches the " +
        "database unvalidated. Pragmatic.Configuration does honour System.ComponentModel.DataAnnotations, " +
        "which is why the same attribute appears to work in one place and not the other.");
}
