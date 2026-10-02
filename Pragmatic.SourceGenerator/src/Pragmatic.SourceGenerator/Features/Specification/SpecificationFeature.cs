using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Specification.Templates;
using Pragmatic.SourceGenerator.Features.Specification.Transforms;

namespace Pragmatic.SourceGenerator.Features.Specification;

/// <summary>
///     Standalone feature: a declared <c>Specification&lt;TEntity&gt;</c> becomes usable where it is
///     consumed — on an <c>IQueryable&lt;TEntity&gt;</c> and on an <c>IReadRepository&lt;TEntity&gt;</c>.
/// </summary>
/// <remarks>
///     <para>
///         No <c>DetectedFeatures</c> flag: the transform resolves
///         <c>Pragmatic.Specification.Specification&lt;T&gt;</c> from the compilation and answers
///         <c>null</c> when the assembly is not referenced. A flag would be a second way to ask the
///         same question, and it could disagree with the first.
///     </para>
/// </remarks>
internal static class SpecificationFeature
{
    public static void Register(IncrementalGeneratorInitializationContext context)
    {
        var declared = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => DeclaredSpecificationTransform.CouldBeASpecification(node),
                transform: static (ctx, ct) => DeclaredSpecificationTransform.Transform(ctx, ct))
            .Where(static m => m is not null && m.IsValid)
            .Select(static (m, _) => m!)
            .Collect();

        context.RegisterSourceOutputSafe(declared, static (spc, models) =>
        {
            if (models.IsDefaultOrEmpty)
                return;

            // One file per (entity, declaring namespace). Grouping by entity alone would give two
            // groups the same hint name where an application declares specifications for one entity in
            // two namespaces — and Roslyn answers a duplicate hint by discarding the whole generator's
            // output with a warning, not an error.
            foreach (var group in models
                         .GroupBy(m => (m.EntityFullTypeName, m.Namespace))
                         .OrderBy(g => g.Key.EntityFullTypeName, System.StringComparer.Ordinal))
            {
                var ordered = group
                    .OrderBy(m => m.MemberName, System.StringComparer.Ordinal)
                    .ThenBy(m => m.Parameters.Count)
                    .ToList();

                var template = new SpecificationExtensionsTemplate(
                    group.Key.EntityFullTypeName,
                    ordered[0].EntityShortName,
                    group.Key.Namespace,
                    ordered);

                var artifact = template.RenderOutput();
                if (!artifact.IsEmpty)
                    spc.AddSource(artifact);
            }
        });
    }
}
