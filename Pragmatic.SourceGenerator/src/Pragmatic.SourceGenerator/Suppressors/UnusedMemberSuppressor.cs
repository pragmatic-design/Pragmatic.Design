using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Suppressors;

/// <summary>
///     Suppresses IDE0051 ("Private member is unused") on private members of <em>partial</em>
///     Pragmatic-decorated types, whose generated half may reference them from code the IDE cannot see
///     at analysis time. Without <c>partial</c> no such half exists and an unused private member is
///     genuinely dead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnusedMemberSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new(
        id: "PRAGS004",
        suppressedDiagnosticId: "IDE0051",
        justification: "Private member may be used by SG-generated partial class code.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(Rule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "IDE0051")
                continue;

            var location = diagnostic.Location;
            var syntaxTree = location.SourceTree;
            if (syntaxTree is null)
                continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var node = root.FindNode(location.SourceSpan);
            var semanticModel = context.GetSemanticModel(syntaxTree);

            var containingType = SuppressionHelper.FindContainingType(node, semanticModel);
            if (SuppressionHelper.IsPragmaticType(containingType) &&
                SuppressionHelper.IsPartialType(containingType))
                context.ReportSuppression(Suppression.Create(Rule, diagnostic));
        }
    }
}
