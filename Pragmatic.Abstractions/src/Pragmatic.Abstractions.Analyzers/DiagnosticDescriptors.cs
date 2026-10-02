using Microsoft.CodeAnalysis;

namespace Pragmatic.Abstractions.Analyzers;

internal static class DiagnosticDescriptors
{
    // Not PRAG1647: the source generator emits that id for "[Inject] is not supported on an
    // open-generic service". Two rules under one id on the same build mean a NoWarn or #pragma
    // silencing one silences the other — and the one silenced by accident would be the one warning
    // that [Inject] members are ignored outright. The id follows the emitter, not the subject
    // ([Inject] belongs to Composition): this analyzer ships in Abstractions, whose range is
    // PRAG1400-1499, and 1647 is in the generator's own range.
    public static readonly DiagnosticDescriptor OptionalInjection = new(
        id: "PRAG1452",
        title: "Injection is optional — a missing service is injected as null",
        messageFormat: "'{0}' uses optional [Inject] (Required = false): a missing service will be injected as null and may cause a NullReferenceException at first use. Set 'Required = true' to fail fast at startup.",
        category: "Pragmatic.Composition",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[Inject] defaults to Required = false for backward compatibility, so an unregistered service is injected as null instead of failing. This hides misconfiguration and risks a NullReferenceException. Set Required = true to opt into fail-fast resolution, or keep Required = false explicitly to acknowledge the optional, nullable contract.");

    public static readonly DiagnosticDescriptor CaptiveDependency = new(
        id: "PRAG1450",
        title: "Captive dependency — scoped service injected into a singleton-lifetime type",
        messageFormat: "'{0}' is a singleton-lifetime host service but injects the scoped service '{1}'. The scoped instance is captured for the app's lifetime (a captive dependency). Inject {2} instead.",
        category: "Pragmatic.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "BackgroundService / IHostedService implementations are registered as singletons. Injecting a scoped service (IOptionsSnapshot<T>, a DbContext, …) directly into their constructor captures one scoped instance forever — stale config, a DbContext shared across all work, and lifetime corruption. Inject IOptionsMonitor<T> for live config, or IServiceScopeFactory and create a scope per unit of work.");

    public static readonly DiagnosticDescriptor MissingJsonContextForBoundaryType = new(
        id: "PRAG2800",
        title: "Serialized type is not covered by a JsonSerializerContext (AOT)",
        messageFormat: "'{0}' is serialized by Pragmatic (message / event / job payload) but no [JsonSerializable(typeof({0}))] appears in this project's JsonSerializerContext. Add it so serialization is AOT-safe once the reflection fallback is disabled.",
        category: "Pragmatic.Serialization",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "For Native AOT, every type the framework serializes must have source-generated metadata in a JsonSerializerContext registered via UseJson(...). Message, domain-event and job-parameter payloads are serialized to/from the outbox, transport and job store. Because Roslyn source generators are not chainable, the Pragmatic generator cannot emit these [JsonSerializable] entries for you — declare a partial JsonSerializerContext in your app and add the flagged types. This diagnostic only fires once your project already declares at least one JsonSerializerContext (i.e. you have opted into source-generated serialization).");

    public static readonly DiagnosticDescriptor BuildServiceProvider = new(
        id: "PRAG1451",
        title: "Avoid BuildServiceProvider() — it creates a second container",
        messageFormat: "'BuildServiceProvider()' builds a second, separate DI container: singletons and options are duplicated and disposables leak. Resolve from the app's provider (constructor injection, or IServiceProvider/IServiceScopeFactory) instead.",
        category: "Pragmatic.DependencyInjection",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Calling IServiceCollection.BuildServiceProvider() during configuration constructs a throwaway container distinct from the one the host builds. Any singleton resolved from it is a different instance than the one the app uses, options/config are bound twice, and registered IDisposables created by it are never disposed. Use the host's service provider via dependency injection, IServiceProvider, or IServiceScopeFactory.");
}
