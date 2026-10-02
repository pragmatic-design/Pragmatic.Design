using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Configuration.Diagnostics;

/// <summary>Diagnostic descriptors for Configuration feature. Range: PRAG2000-PRAG2099</summary>
internal static class ConfigurationDiagnostics
{
    // PRAG2000 (configuration class must be partial) is the companion analyzer's, which reports it on the
    // declaration (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    public static readonly DiagnosticDescriptor MustNotBeStaticOrAbstract = DiagnosticFactory.Error(
        "PRAG2001", "Configuration class cannot be static or abstract",
        "Type '{0}' cannot be static or abstract when using [Configuration]",
        "Remove the static or abstract modifier from the class declaration.");

    public static readonly DiagnosticDescriptor InvariantMustBeCallable = DiagnosticFactory.Error(
        "PRAG2002", "[ConfigInvariant] method cannot be called",
        "[ConfigInvariant] method '{0}' on '{1}' is never run: it must be a parameterless instance method returning bool",
        "Make the method non-static, remove its parameters and return bool — the validator calls it on the bound options instance.");

    public static readonly DiagnosticDescriptor RequiredWithDefault = DiagnosticFactory.Warning(
        "PRAG2050", "Required property has default value",
        "Property '{0}' on '{1}' has [Required] but also has a default value — the default will satisfy the requirement",
        "Consider whether [Required] is needed when a default value is provided.");
}
