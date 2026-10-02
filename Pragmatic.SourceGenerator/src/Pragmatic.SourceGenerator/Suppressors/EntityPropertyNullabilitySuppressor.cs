using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.SourceGenerator.Suppressors;

/// <summary>
///     Suppresses CS8618 ("Non-nullable field/property must contain a non-null value when exiting constructor")
///     on the entity properties the SG-generated <c>Create()</c> factory and the trait templates actually
///     initialize: properties with a non-public setter on a partial <c>[Entity]</c>.
///     A public setter or a field is the developer's own — the generator never writes it, so the
///     warning there is genuine and stays visible.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EntityPropertyNullabilitySuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor Rule = new(
        id: "PRAGS001",
        suppressedDiagnosticId: "CS8618",
        justification: "Entity properties are initialized by the SG-generated Create() factory method and trait templates.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(Rule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "CS8618")
                continue;

            var location = diagnostic.Location;
            var syntaxTree = location.SourceTree;
            if (syntaxTree is null)
                continue;

            var root = syntaxTree.GetRoot(context.CancellationToken);
            var node = root.FindNode(location.SourceSpan);
            var semanticModel = context.GetSemanticModel(syntaxTree);

            // The factory only assigns properties it owns; anything else (fields, public setters,
            // the constructor itself) is hand-written surface.
            if (SuppressionHelper.FindDeclaredMember(node, semanticModel) is not IPropertySymbol property)
                continue;

            if (property.SetMethod is not { } setter)
                continue;

            if (setter.DeclaredAccessibility == Accessibility.Public && !setter.IsInitOnly)
                continue;

            var containingType = property.ContainingType;
            if (!SuppressionHelper.IsEntityType(containingType) ||
                !SuppressionHelper.IsPartialType(containingType))
                continue;

            context.ReportSuppression(Suppression.Create(Rule, diagnostic));
        }
    }
}
