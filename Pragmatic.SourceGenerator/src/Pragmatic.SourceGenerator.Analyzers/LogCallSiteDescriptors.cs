using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Diagnostics for Pragmatic log call sites. Range PRAG2400–PRAG2449 (Logging).
/// </summary>
/// <remarks>
///     Reported here rather than by the generator because the generator has no location to give: a
///     method it cannot write a body for is left alone, and these say why, where the method is written.
/// </remarks>
internal static class LogCallSiteDescriptors
{
    private const string Category = "Pragmatic.Logging";

    public static readonly DiagnosticDescriptor WrongShape = new(
        "PRAG2400",
        "A log call site must be a partial void method",
        "'{0}' cannot get a generated body: a [LoggerMessage] method is a non-generic 'partial void' declaration whose parameters are passed by value",
        Category, DiagnosticSeverity.Error, true,
        "The generator writes the implementation part of the method; it can only do that for the shape Microsoft's generator accepts too.");

    public static readonly DiagnosticDescriptor PlaceholderWithoutParameter = new(
        "PRAG2401",
        "A placeholder names no parameter",
        "The template's '{{{0}}}' matches no parameter of the method",
        Category, DiagnosticSeverity.Error, true,
        "A placeholder is matched to a parameter by name, ignoring case and a leading '@'. Rename one of the two, or add the parameter.");

    public static readonly DiagnosticDescriptor ParameterNotInTemplate = new(
        "PRAG2402",
        "A parameter is not in the message",
        "Parameter '{0}' is not named by the template; it is logged as a structured property but not in the message",
        Category, DiagnosticSeverity.Warning, true,
        "Usually a missing placeholder. Microsoft's generator warns about the same thing (SYSLIB1015).");

    public static readonly DiagnosticDescriptor NoLogger = new(
        "PRAG2403",
        "A log call site has no logger",
        "'{0}' has no logger: a static method takes an ILogger parameter, an instance method finds exactly one ILogger parameter, field, property or primary-constructor parameter",
        Category, DiagnosticSeverity.Error, true,
        "Two loggers in the type is a choice the generator does not make; pass the one to use as a parameter.");

    public static readonly DiagnosticDescriptor NoLevel = new(
        "PRAG2404",
        "A log call site has no level",
        "'{0}' sets no Level and takes no LogLevel parameter",
        Category, DiagnosticSeverity.Error, true,
        "Set Level on the attribute, or take the level as a LogLevel parameter.");

    public static readonly DiagnosticDescriptor DuplicateEventId = new(
        "PRAG2405",
        "Two log call sites share an event id",
        "'{0}' uses event id {1}, which '{2}' in the same type uses too",
        Category, DiagnosticSeverity.Warning, true,
        "An event id is how an operator filters or alerts on one kind of entry; two kinds under one id are indistinguishable. A call site that sets no id gets one derived from its event name.",
        helpLinkUri: null,
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MalformedTemplate = new(
        "PRAG2406",
        "The message template is malformed",
        "The template of '{0}' cannot be used: {1}",
        Category, DiagnosticSeverity.Error, true,
        "Placeholders are '{Name}' or '{Name:format}'; a literal brace is written '{{' or '}}'.");

    public static readonly DiagnosticDescriptor ContainerNotPartial = new(
        "PRAG2407",
        "The type of a log call site is not partial",
        "'{0}' must be partial: the generated body of its log call site is written into it",
        Category, DiagnosticSeverity.Error, true,
        "Every type that encloses the method, the declaring one included, is reopened by the generated file, and C# reopens only a partial type.");

    public static readonly DiagnosticDescriptor MicrosoftAttributeBound = new(
        "PRAG2408",
        "[LoggerMessage] binds to Microsoft's attribute in a project that uses Pragmatic call sites",
        "'[{0}]' on '{1}' binds to Microsoft.Extensions.Logging.LoggerMessageAttribute: the global alias that selects Pragmatic's did not reach this project, so Microsoft's generator owns the method and its parameters' [NotLogged] and [PersonalData] are not applied",
        Category, DiagnosticSeverity.Error, true,
        "The Pragmatic.SourceGenerator package declares 'global using LoggerMessageAttribute = global::Pragmatic.Logging.CallSites.LoggerMessageAttribute;' through MSBuild. If it is missing — a <Using Remove>, a project that imports the props differently — restore it. To hand this one method to Microsoft's generator on purpose, write the attribute fully qualified: [Microsoft.Extensions.Logging.LoggerMessage].");

    public static readonly DiagnosticDescriptor MaskOutsideCallSite = new(
        "PRAG2410",
        "[NotLogged] or [PersonalData] on a parameter does nothing here",
        "[{0}] on parameter '{1}' of '{2}' is not applied: only a parameter of a Pragmatic [LoggerMessage] method is masked{3}",
        Category, DiagnosticSeverity.Error, true,
        "On a parameter the attribute is read by the log call site generator, and by nothing else. Anywhere else it compiles and changes nothing, which is how a value believed masked reaches a log in clear.");
}
