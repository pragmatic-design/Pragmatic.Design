using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Analyzers;

/// <summary>
///     Reports what is wrong with a Pragmatic log call site, the guard against losing the alias that
///     selects one, and a masking attribute on a parameter that nothing reads.
/// </summary>
/// <remarks>
///     <para>
///         The call-site rules are <see cref="LogCallSiteReader" />'s, the same code the generator runs:
///         a method reported here is exactly one the generator wrote no body for.
///     </para>
///     <para>
///         <b>The guard (PRAG2408).</b> With the alias missing, the same source compiles cleanly against
///         Microsoft's attribute: its generator writes the body and the masking attributes on the
///         parameters are ignored. That is a change of behaviour with no error, so in a project that uses
///         Pragmatic call sites (<c>PragmaticLogCallSites=true</c>, which the generator's package sets) a
///         <c>[LoggerMessage]</c> written by its simple name that binds to Microsoft's is an error.
///         Written fully qualified it is a deliberate opt-out, and not reported.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LogCallSiteAnalyzer : DiagnosticAnalyzer
{
    private const string PragmaticAttribute = "Pragmatic.Logging.CallSites.LoggerMessageAttribute";
    private const string MicrosoftAttribute = "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string CallSitesProperty = "build_property.PragmaticLogCallSites";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        LogCallSiteDescriptors.WrongShape,
        LogCallSiteDescriptors.PlaceholderWithoutParameter,
        LogCallSiteDescriptors.ParameterNotInTemplate,
        LogCallSiteDescriptors.NoLogger,
        LogCallSiteDescriptors.NoLevel,
        LogCallSiteDescriptors.DuplicateEventId,
        LogCallSiteDescriptors.MalformedTemplate,
        LogCallSiteDescriptors.ContainerNotPartial,
        LogCallSiteDescriptors.MicrosoftAttributeBound,
        LogCallSiteDescriptors.MaskOutsideCallSite);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var pragmatic = context.Compilation.GetTypeByMetadataName(PragmaticAttribute);
        var microsoft = context.Compilation.GetTypeByMetadataName(MicrosoftAttribute);

        var guardOn = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                          .TryGetValue(CallSitesProperty, out var value)
                      && string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);

        // Event ids per type, gathered as methods are analyzed and compared at the end.
        var eventIds = new ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(int Id, IMethodSymbol Method)>>(
            SymbolEqualityComparer.Default);

        context.RegisterSymbolAction(c =>
        {
            var method = (IMethodSymbol)c.Symbol;

            ReportMaskOutsideCallSite(c, method, pragmatic);

            if (pragmatic is not null
                && method.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, pragmatic)) is { } attribute)
            {
                AnalyzeCallSite(c, method, attribute, eventIds);
            }

            if (guardOn && microsoft is not null)
                ReportMicrosoftBinding(c, method, microsoft);
        }, SymbolKind.Method);

        context.RegisterCompilationEndAction(c => ReportDuplicateEventIds(c, eventIds));
    }

    private static void AnalyzeCallSite(
        SymbolAnalysisContext context,
        IMethodSymbol method,
        AttributeData attribute,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(int, IMethodSymbol)>> eventIds)
    {
        // The definition part only: the generated implementation part is the same method.
        if (!method.IsPartialDefinition && method.PartialDefinitionPart is not null)
            return;

        if (LogCallSiteReader.Read(method, attribute, context.Compilation) is not { } shape)
            return;

        var location = method.Locations.FirstOrDefault() ?? Location.None;
        foreach (var problem in shape.Problems)
        {
            var diagnostic = problem.Kind switch
            {
                LogCallSiteProblemKind.WrongShape => Diagnostic.Create(LogCallSiteDescriptors.WrongShape, location, method.Name),
                LogCallSiteProblemKind.PlaceholderWithoutParameter => Diagnostic.Create(LogCallSiteDescriptors.PlaceholderWithoutParameter, AttributeLocation(attribute, location), problem.Subject),
                LogCallSiteProblemKind.ParameterNotInTemplate => Diagnostic.Create(LogCallSiteDescriptors.ParameterNotInTemplate, ParameterLocation(method, problem.Subject, location), problem.Subject),
                LogCallSiteProblemKind.NoLogger => Diagnostic.Create(LogCallSiteDescriptors.NoLogger, location, method.Name),
                LogCallSiteProblemKind.NoLevel => Diagnostic.Create(LogCallSiteDescriptors.NoLevel, location, method.Name),
                LogCallSiteProblemKind.MalformedTemplate => Diagnostic.Create(LogCallSiteDescriptors.MalformedTemplate, AttributeLocation(attribute, location), method.Name, problem.Subject),
                LogCallSiteProblemKind.ContainerNotPartial => Diagnostic.Create(LogCallSiteDescriptors.ContainerNotPartial, location, problem.Subject),
                _ => null,
            };

            if (diagnostic is not null)
                context.ReportDiagnostic(diagnostic);
        }

        var eventName = shape.EventName ?? method.Name;
        var id = shape.EventId >= 0 ? shape.EventId : LogEventIds.Derive(eventName);
        eventIds.GetOrAdd(method.ContainingType, _ => []).Add((id, method));
    }

    private static void ReportDuplicateEventIds(
        CompilationAnalysisContext context,
        ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<(int Id, IMethodSymbol Method)>> eventIds)
    {
        foreach (var type in eventIds.Values)
        {
            // Ordered by position so the first one written keeps the id and the later ones are reported.
            var byId = type
                .OrderBy(e => e.Method.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0)
                .GroupBy(e => e.Id)
                .Where(g => g.Count() > 1);

            foreach (var group in byId)
            {
                var first = group.First().Method;
                foreach (var (id, method) in group.Skip(1))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        LogCallSiteDescriptors.DuplicateEventId,
                        method.Locations.FirstOrDefault() ?? Location.None,
                        method.Name, id, first.Name));
                }
            }
        }
    }

    private static void ReportMicrosoftBinding(SymbolAnalysisContext context, IMethodSymbol method, INamedTypeSymbol microsoft)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, microsoft))
                continue;

            // Fully qualified is the opt-out; the simple name is the alias that did not arrive.
            if (attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken) is not AttributeSyntax { Name: IdentifierNameSyntax name } syntax)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                LogCallSiteDescriptors.MicrosoftAttributeBound, syntax.GetLocation(), name.Identifier.ValueText, method.Name));
        }
    }

    private static void ReportMaskOutsideCallSite(SymbolAnalysisContext context, IMethodSymbol method, INamedTypeSymbol? pragmatic)
    {
        var isCallSite = pragmatic is not null
                         && method.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, pragmatic));
        if (isCallSite)
            return;

        foreach (var parameter in method.Parameters)
        {
            foreach (var attribute in parameter.GetAttributes())
            {
                var name = attribute.AttributeClass?.Name;
                var ns = attribute.AttributeClass?.ContainingNamespace?.ToDisplayString();
                var masking = (name == "NotLoggedAttribute" && ns == "Pragmatic")
                              || (name == "PersonalDataAttribute" && ns == "Pragmatic.Privacy");
                if (!masking)
                    continue;

                // A positional record's parameter: the attribute was meant for the property it declares.
                var hint = method is { MethodKind: MethodKind.Constructor, ContainingType.IsRecord: true }
                    ? $"; on a positional record write [property: {Shorten(name!)}] so it reaches the property"
                    : "";

                var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                               ?? parameter.Locations.FirstOrDefault()
                               ?? Location.None;

                context.ReportDiagnostic(Diagnostic.Create(
                    LogCallSiteDescriptors.MaskOutsideCallSite, location, Shorten(name!), parameter.Name, method.Name, hint));
            }
        }
    }

    private static string Shorten(string attributeName)
        => attributeName.EndsWith("Attribute", System.StringComparison.Ordinal)
            ? attributeName.Substring(0, attributeName.Length - "Attribute".Length)
            : attributeName;

    private static Location AttributeLocation(AttributeData attribute, Location fallback)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? fallback;

    private static Location ParameterLocation(IMethodSymbol method, string name, Location fallback)
        => method.Parameters.FirstOrDefault(p => p.Name == name)?.Locations.FirstOrDefault() ?? fallback;
}
