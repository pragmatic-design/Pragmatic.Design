using System;
using Microsoft.CodeAnalysis;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     Crash-isolation wrappers around <c>RegisterSourceOutput</c>. The Pragmatic source generator is a
///     single <c>IIncrementalGenerator</c> with ~100 output registrations; without a guard, an unhandled
///     exception in ANY one output's template/transform surfaces as a cryptic CS8785 that suppresses the
///     output of EVERY feature. These wrappers catch the failure, report it as PRAG9000, and let
///     the other outputs proceed.
///     <para>
///         Known limit: this does NOT cover duplicate hint names. Roslyn detects those while merging the
///         sources of different output registrations — outside this try/catch — and responds by discarding
///         the whole generator's output with a CS8785 <em>warning</em>, which a project that does not treat
///         warnings as errors will not even fail on. Uniqueness has to be guaranteed upstream, by passing
///         the namespace to <c>VirtualFolderHints.ForType</c>/<c>ForEntityConfig</c>.
///     </para>
/// </summary>
internal static class SafeSourceOutput
{
    // RS2008: this shared descriptor is linked into several SG assemblies; release-tracking the id in
    // each would be brittle, so suppress the tracking requirement for this single infra diagnostic.
#pragma warning disable RS2008 // Enable analyzer release tracking
    private static readonly DiagnosticDescriptor FeatureFailed = new(
        id: "PRAG9000",
        title: "Source generator output failed",
        messageFormat: "A Pragmatic source generator output failed ({0}): {1}. The code it should have "
                       + "generated is missing, so expect unresolved-type errors that point at the "
                       + "symptom rather than this cause. Other generated outputs are unaffected; "
                       + "please report this with the triggering code.",
        category: "Pragmatic.SourceGenerator",
        // Error, not Warning: a failed output means generated code that the consumer's source refers to
        // simply is not there. Reporting that as a warning lets the build proceed to a cascade of CS0246s
        // whose real cause is buried in warning output — the failure has to surface where it happens.
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    /// <summary>Crash-isolating equivalent of <c>context.RegisterSourceOutput(source, action)</c>.</summary>
    public static void RegisterSourceOutputSafe<TSource>(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<TSource> source,
        Action<SourceProductionContext, TSource> action)
        => context.RegisterSourceOutput(source, (spc, src) => Guarded(spc, src, action));

    /// <summary>Crash-isolating equivalent of <c>context.RegisterSourceOutput(source, action)</c>.</summary>
    public static void RegisterSourceOutputSafe<TSource>(
        this IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<TSource> source,
        Action<SourceProductionContext, TSource> action)
        => context.RegisterSourceOutput(source, (spc, src) => Guarded(spc, src, action));

    private static void Guarded<TSource>(
        SourceProductionContext spc, TSource src, Action<SourceProductionContext, TSource> action)
    {
        try
        {
            action(spc, src);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException or StackOverflowException))
        {
            spc.ReportDiagnostic(Diagnostic.Create(FeatureFailed, Location.None, ex.GetType().Name, ex.Message));
        }
    }
}
