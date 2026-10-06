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

        context.RegisterSourceOutputSafe(callSites, static (ctx, all) =>
        {
            foreach (var type in all.GroupBy(static c => c.TypeKey, System.StringComparer.Ordinal))
            {
                var artifact = new LogCallSitesTemplate(type.ToList()).RenderOutput();
                if (!artifact.IsEmpty)
                    ctx.AddSource(artifact);
            }
        });
    }
}
