using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Diagnostic descriptors for "class must be partial" across all Pragmatic modules — the only
///     declaration of each of these IDs.
/// </summary>
/// <remarks>
///     Every descriptor here must be wired in <see cref="NotPartialClassAnalyzer" /> (both in the
///     attribute map and in <c>SupportedDiagnostics</c>); a descriptor nothing reports is a promise the
///     IDE does not keep, and leaves the matching entry in <c>MakeClassPartialCodeFixProvider</c> with
///     nothing to attach to. The generator declares none of them.
/// </remarks>
internal static class NotPartialDiagnosticDescriptors
{
    private const string Category = "Pragmatic.Design";

    // Mapping of Pragmatic attribute FQN patterns to their diagnostic IDs.
    // The analyzer detects these attributes on non-partial classes and reports the corresponding diagnostic.

    // PRAG0200: Validation (any validation attribute on a non-partial type)
    public static readonly DiagnosticDescriptor Prag0200 = Create(
        "PRAG0200", "Type with validation attributes must be partial",
        "Type '{0}' has validation attributes but is not declared as partial");

    // PRAG0300: Mapping ([MapFrom], [MapTo], etc.)
    public static readonly DiagnosticDescriptor Prag0300 = Create(
        "PRAG0300", "Type must be partial",
        "Type '{0}' must be declared as partial to use [{1}]");

    // PRAG0400: Actions ([Query], [Mutation], [SideEffect])
    public static readonly DiagnosticDescriptor Prag0400 = Create(
        "PRAG0400", "Action class must be partial",
        "Action class '{0}' must be declared as partial");

    // PRAG0406: Actions ([Boundary])
    public static readonly DiagnosticDescriptor Prag0406 = Create(
        "PRAG0406", "[Boundary] class must be partial",
        "[Boundary] class '{0}' must be partial");

    // PRAG0500: Endpoints ([Endpoint])
    public static readonly DiagnosticDescriptor Prag0500 = Create(
        "PRAG0500", "Endpoint class must be partial",
        "Endpoint class '{0}' must be declared as partial");

    // PRAG0600: Persistence ([Entity], [Repository], etc.)
    public static readonly DiagnosticDescriptor Prag0600 = Create(
        "PRAG0600", "Type must be partial",
        "Type '{0}' must be declared as partial to use [{1}]");

    // PRAG0602: Persistence ([Database])
    public static readonly DiagnosticDescriptor Prag0602 = Create(
        "PRAG0602", "Database context must be partial",
        "Type '{0}' with [Database] attribute must be declared as partial");

    // PRAG0712: Persistence queries ([Query<TEntity>], [Query<TEntity, TResult>]). Owned here and not by the
    // generator. A non-partial query is not an action: reporting it as PRAG0400 ("Action class
    // must be partial") would put a second, wrong diagnostic beside PRAG0712.
    public static readonly DiagnosticDescriptor Prag0712 = Create(
        "PRAG0712", "[Query] on a type that is not partial",
        "'{0}' declares [Query] but is not partial, so nothing is generated for it");

    // PRAG0801: Messaging ([MessageHandler])
    public static readonly DiagnosticDescriptor Prag0801 = Create(
        "PRAG0801", "Message handler must be partial",
        "Type '{0}' is decorated with [MessageHandler] but is not declared as partial. " +
        "The source generator cannot generate pipeline code without the partial modifier.");

    // PRAG1100: Ownership ([HasOwner])
    public static readonly DiagnosticDescriptor Prag1100 = Create(
        "PRAG1100", "[HasOwner] requires partial class",
        "Type '{0}' with [HasOwner] must be declared as partial for code generation");

    // PRAG1700: Caching ([Cached])
    public static readonly DiagnosticDescriptor Prag1700 = Create(
        "PRAG1700", "Type must be partial",
        "Type '{0}' must be declared as partial to use [{1}]");

    // PRAG2200: Patch ([GeneratePatch])
    // The analyzer only knows the attribute, so the entity is left as <T> rather than rendering
    // "[GeneratePatch<GeneratePatch>]".
    public static readonly DiagnosticDescriptor Prag2200 = Create(
        "PRAG2200", "Patch type must be partial",
        "Type '{0}' annotated with [GeneratePatch<T>] must be declared as partial");

    // PRAG2000: Configuration ([PragmaticConfiguration])
    public static readonly DiagnosticDescriptor Prag2000 = Create(
        "PRAG2000", "Configuration class must be partial",
        "Type '{0}' must be declared as partial to use [{1}]");

    // PRAG2502: Jobs ([Job], [RecurringJob]) — an Error. It was a Warning on the claim
    // that the build would not fail, but the generator emitted `partial class {Type}` for a non-partial
    // job all the same, which is CS0260. Now the generator skips the job, so without `partial` it is
    // never registered and never runs: an error, like every other "must be partial".
    public static readonly DiagnosticDescriptor Prag2502 = Create(
        "PRAG2502", "Job class must be partial",
        "Type '{0}' is decorated with [Job]/[RecurringJob] but is not declared as partial, so no invoker is generated and the job never runs");

    private static DiagnosticDescriptor Create(
        string id, string title, string messageFormat, DiagnosticSeverity severity = DiagnosticSeverity.Error)
        => new(id, title, messageFormat, Category, severity, true,
            helpLinkUri: "https://docs.pragmaticdesign.net/reference/diagnostics/");
}
