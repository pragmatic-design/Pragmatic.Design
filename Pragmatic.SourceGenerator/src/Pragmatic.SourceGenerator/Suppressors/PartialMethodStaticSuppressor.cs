using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Suppressors;

/// <summary>
///     Suppresses CA1822 ("Member does not access instance data and can be marked as static") only
///     where the analyzer really is missing half the picture. CA1822 reasons about what the member
///     itself touches, not about its callers, so a plain hand-written method that ignores instance
///     state is a genuine finding — the generated half of the type changes nothing about it.
///     Two cases remain: a <c>partial</c> method whose other declaration lives in generated code, and
///     a member that is itself generated. Both require a partial Pragmatic type.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PartialMethodStaticSuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new(
        id: "PRAGS002",
        suppressedDiagnosticId: "CA1822",
        justification: "Method is in a partial class with SG-generated members that may use instance data.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(Rule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CA1822")
                continue;

            var location = diagnostic.Location;
            var syntaxTree = location.SourceTree;
            if (syntaxTree is null)
                continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var node = root.FindNode(location.SourceSpan);
            var semanticModel = context.GetSemanticModel(syntaxTree);

            var containingType = SuppressionHelper.FindContainingType(node, semanticModel);
            if (!SuppressionHelper.IsPragmaticType(containingType) ||
                !SuppressionHelper.IsPartialType(containingType))
                continue;

            if (!SuppressionHelper.IsGeneratedLocation(location) &&
                !IsPartialMethod(SuppressionHelper.FindDeclaredMember(node, semanticModel)))
                continue;

            context.ReportSuppression(Suppression.Create(Rule, diagnostic));
        }
    }

    private static bool IsPartialMethod(ISymbol? symbol)
        => symbol is IMethodSymbol method &&
           (method.PartialDefinitionPart != null || method.PartialImplementationPart != null);
}
