using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Suppressors;

/// <summary>
///     Suppresses CA1062 ("Validate arguments of public methods") inside SG-generated files of
///     Pragmatic-decorated types: generated invokers and repositories get their dependencies from the
///     generated DI constructor, so the null checks the rule asks for are already guaranteed.
///     In a hand-written file CA1062 is a real finding and stays visible, even when the developer's
///     code sits in the other half of the same partial type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GeneratedParameterValidationSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new(
        id: "PRAGS003",
        suppressedDiagnosticId: "CA1062",
        justification: "Parameters in Pragmatic-generated types are validated by the SG-generated DI constructor.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(Rule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CA1062")
                continue;

            var location = diagnostic.Location;
            var syntaxTree = location.SourceTree;
            if (syntaxTree is null)
                continue;

            if (!SuppressionHelper.IsGeneratedLocation(location))
                continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var node = root.FindNode(location.SourceSpan);
            var semanticModel = context.GetSemanticModel(syntaxTree);

            var containingType = SuppressionHelper.FindContainingType(node, semanticModel);
            if (containingType != null && SuppressionHelper.IsPragmaticType(containingType))
                context.ReportSuppression(Suppression.Create(Rule, diagnostic));
        }
    }
}
