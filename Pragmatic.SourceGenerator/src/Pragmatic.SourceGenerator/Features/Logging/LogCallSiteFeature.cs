using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Logging.Templates;
using Pragmatic.SourceGenerator.Features.Logging.Transforms;

namespace Pragmatic.SourceGenerator.Features.Logging;

/// <summary>
///     Writes the body of every <c>[LoggerMessage]</c> method bound to
///     <c>Pragmatic.Logging.CallSites.LoggerMessageAttribute</c>, and the state it logs.
/// </summary>
/// <remarks>
///     <para>
///         A method binds here through the global alias the generator's package declares; written fully
///         qualified as <c>Microsoft.Extensions.Logging.LoggerMessage</c> it stays Microsoft's. One file per
///         type, so the call sites of a type share one reopening of it.
///     </para>
///     <para>
///         A method that cannot be generated — not partial, not void, no logger, a placeholder with no
///         parameter — is left alone. The analyzer reports it where it is written; reporting from here
///         would have no location to give.
///     </para>
/// </remarks>
internal static class LogCallSiteFeature
{
    public const string AttributeFqn = "Pragmatic.Logging.CallSites.LoggerMessageAttribute";

    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var callSites = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFqn,
                static (node, _) => node is MethodDeclarationSyntax,
                LogCallSiteTransform.Transform)
            .Where(static m => m is { IsValid: true })
            .Select(static (m, _) => m!)
            .Collect();

        var assemblyName = context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "");

        context.RegisterSourceOutputSafe(callSites.Combine(assemblyName), static (ctx, input) =>
        {
            var (all, assembly) = input;
            foreach (var type in all.GroupBy(static c => c.TypeKey, System.StringComparer.Ordinal))
            {
                var artifact = new LogCallSitesTemplate(type.ToList()).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }

            // The writers the call sites name, once for the assembly: two call sites logging the same type
            // share its methods. In the namespace the call sites were told, the redaction map's.
            var writers = all
                .SelectMany(static c => c.Parameters)
                .SelectMany(static p => p.JsonWriterMethods)
                .ToList();
            if (writers.Count == 0)
                return;

            var file = new Serialization.Templates.Utf8JsonWritersTemplate(
                Redaction.Templates.RedactionMapTemplate.NamespaceFor(assembly), writers).RenderOutput();
            if (!file.IsEmpty)
                ctx.AddSource(file);
        });
    }
}
