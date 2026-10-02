using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Composition;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Resilience.Templates;

namespace Pragmatic.SourceGenerator.Features.Resilience;

/// <summary>
///     Publishes whether an assembly <b>declares</b> resilience, so a host wires the capability
///     because somebody asked for it rather than because the package is on the compilation.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ This exists because presence is not a question worth asking. Every probe in
///         <c>FeatureDetector</c> is <c>TypeExists</c>, and an assembly is on the compilation whether
///         the application asked for it or somebody else's dependency brought it. Measured on a
///         consumer application: its host used resilience in <b>zero</b> files and still got
///         <c>services.AddPragmaticResilience()</c>, and removing the package reference changed
///         nothing because the package arrived transitively anyway.
///     </para>
///     <para>
///         Direct-versus-transitive cannot decide it either — that host referenced the package
///         directly <em>and</em> received it transitively — and both variants fail in the dangerous
///         direction: a capability the application asked for, classified as transitive, silently not
///         wired. "Does anybody declare this" is a source question, and source is what a generator
///         reads.
///     </para>
///     <para>
///         Two channels, and both are needed: the assembly attribute is how a <b>referenced module</b>
///         is discovered, and the <see cref="MetadataEntry" /> returned here is how the <b>host's own</b>
///         declarations reach the same aggregation — the attribute this run emits is not on the symbol
///         yet, and the reader only walks <c>compilation.References</c>.
///     </para>
///     <para>
///         Nothing is emitted when nothing is declared. An empty document would put the category on
///         the compilation and put the question back where it started.
///     </para>
/// </remarks>
internal static class ResilienceFeature
{
    /// <summary>The four ways an assembly asks for resilience. All of them count.</summary>
    private static readonly string[] DeclarationAttributes =
    [
        "Pragmatic.Resilience.Attributes.ResiliencePolicyAttribute",
        "Pragmatic.Resilience.Attributes.RetryAttribute",
        "Pragmatic.Resilience.Attributes.TimeoutAttribute",
        "Pragmatic.Resilience.Attributes.CircuitBreakerAttribute",
    ];

    public static IncrementalValueProvider<EquatableArray<MetadataEntry>> Register(
        IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DeclarationAttributes[0],
                static (_, _) => true,
                static (ctx, _) => ctx.TargetSymbol.Name)
            .Collect();

        for (var i = 1; i < DeclarationAttributes.Length; i++)
        {
            var more = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    DeclarationAttributes[i],
                    static (_, _) => true,
                    static (ctx, _) => ctx.TargetSymbol.Name)
                .Collect();

            declarations = declarations.Combine(more).Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        }

        ReportAttributesNothingReads(context);

        var withAssembly = declarations
            .Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? ""))
            .Select(static (pair, _) => (Count: pair.Left.Length, Assembly: pair.Right));

        context.RegisterSourceOutputSafe(withAssembly, static (ctx, data) =>
        {
            if (data.Count == 0)
                return;

            ctx.AddSource(new ResilienceMetadataTemplate(data.Count).RenderOutput());
        });

        return withAssembly.Select(static (data, _) => data.Count == 0
            ? EquatableArray<MetadataEntry>.Empty
            : new EquatableArray<MetadataEntry>(ImmutableArray.Create(
                HostLocalRegistration.CreatePayload(
                    MetadataCategoryIds.Resilience,
                    "1.0",
                    ResilienceMetadataTemplate.Payload(data.Count)))));
    }

    private const string JobAttribute = "Pragmatic.Jobs.Attributes.JobAttribute";
    private const string RecurringJobAttribute = "Pragmatic.Jobs.Attributes.RecurringJobAttribute";
    private const string MessageHandlerAttribute = "Pragmatic.Messaging.Attributes.MessageHandlerAttribute";

    /// <summary>
    ///     PRAG0464 — a <c>[Retry]</c>, <c>[Timeout]</c> or <c>[CircuitBreaker]</c> on a class none of the
    ///     engines that read it would ever see.
    /// </summary>
    /// <remarks>
    ///     The readers are <c>JobTransform</c> (<c>[Retry]</c>, <c>[Timeout]</c>) and
    ///     <c>MessageHandlerTransform</c> (all three). The attribute is still counted as a declaration
    ///     above: that says the application asked for resilience, which it did.
    /// </remarks>
    private static void ReportAttributesNothingReads(IncrementalGeneratorInitializationContext context)
    {
        Report(context, DeclarationAttributes[1], "Retry", readByJobs: true);
        Report(context, DeclarationAttributes[2], "Timeout", readByJobs: true);
        Report(context, DeclarationAttributes[3], "CircuitBreaker", readByJobs: false);
    }

    private static void Report(
        IncrementalGeneratorInitializationContext context, string attributeFqn, string shortName, bool readByJobs)
    {
        var unread = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                attributeFqn,
                static (_, _) => true,
                (ctx, _) =>
                {
                    var carried = ctx.TargetSymbol.GetAttributes()
                        .Select(a => a.AttributeClass?.ToDisplayString())
                        .ToImmutableHashSet();

                    if (carried.Contains(MessageHandlerAttribute))
                        return null;
                    if (readByJobs && (carried.Contains(JobAttribute) || carried.Contains(RecurringJobAttribute)))
                        return null;

                    var why = readByJobs
                        ? $"[{shortName}] is read on a [Job], a [RecurringJob] or a [MessageHandler]"
                        : $"[{shortName}] is read on a [MessageHandler] only";

                    return new UnreadAttribute(
                        shortName, ctx.TargetSymbol.Name, why,
                        LocationInfo.From(ctx.Attributes[0].ApplicationSyntaxReference?.GetSyntax().GetLocation()
                                          ?? ctx.TargetNode.GetLocation()));
                })
            .Where(static u => u is not null);

        context.RegisterSourceOutputSafe(unread, static (ctx, u) =>
            ctx.ReportDiagnostic(Diagnostic.Create(
                Actions.Diagnostics.ActionsDiagnostics.ResilienceAttributeNothingReads,
                u!.Location?.ToLocation() ?? Location.None,
                u.Attribute, u.TypeName, u.Why)));
    }

    /// <summary>A resilience attribute nothing reads, where it is written.</summary>
    private sealed record UnreadAttribute(string Attribute, string TypeName, string Why, LocationInfo? Location);
}
