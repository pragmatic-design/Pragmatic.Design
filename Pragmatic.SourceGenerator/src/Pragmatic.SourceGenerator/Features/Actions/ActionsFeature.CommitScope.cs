using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     PRAG0424: an invocation that commits into more than one boundary, with no decision recorded.
/// </summary>
internal static partial class ActionsFeature
{
    /// <summary>
    ///     PRAG0426: a declared transaction that reaches a boundary it cannot roll back.
    /// </summary>
    /// <remarks>
    ///     Reported instead of PRAG0424, not alongside it: the two ask different things. PRAG0424 asks
    ///     for a decision about writes that will outlive a failure; this one says the decision already
    ///     made — <c>[Transactional]</c> — cannot be honoured.
    /// </remarks>
    private static bool ReportTransactionCrossesBoundary(
        SourceProductionContext context,
        string typeName,
        Location? location,
        bool isTransactional,
        EquatableArray<string> foreignSteps)
    {
        if (!isTransactional || foreignSteps.IsDefaultOrEmpty)
            return false;

        context.ReportDiagnostic(Diagnostic.Create(
            ActionsDiagnostics.TransactionCrossesBoundary, location, typeName,
            string.Join(", ", foreignSteps)));

        return true;
    }

    private static void ReportUnrecordedPartialWriteRisk(
        SourceProductionContext context,
        string typeName,
        Location? location,
        int commitScopeCount,
        EquatableArray<string> foreignFacades,
        bool acceptsPartialWrites)
    {
        // One store is atomic — the whole point is the second one. And a recorded decision is the
        // outcome this diagnostic exists to produce, so it silences it.
        if (commitScopeCount < 2 || acceptsPartialWrites || foreignFacades.IsDefaultOrEmpty)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            ActionsDiagnostics.UnrecordedPartialWriteRisk, location, typeName,
            string.Join(", ", foreignFacades)));
    }

    /// <summary>
    ///     PRAG0429: the undo declared on a cross-boundary step, and what it does not give you.
    /// </summary>
    private static void ReportCompensationIsNotDurable(
        SourceProductionContext context,
        string typeName,
        Location? location,
        EquatableArray<string> compensatedSteps)
    {
        if (compensatedSteps.IsDefaultOrEmpty)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            ActionsDiagnostics.CompensationIsNotDurable, location, typeName,
            string.Join(", ", compensatedSteps)));
    }

    /// <summary>
    ///     PRAG0427: a composite that orchestrates nothing.
    /// </summary>
    private static void ReportCompositeDiagnostics(
        SourceProductionContext context, Models.CompositeActionModel model)
    {
        // PRAG0430: PerStep contradicts what a composite is, and the invoker cannot honour it.
        if (model.DeclaresPerStep)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.PerStepOnComposite, model.Location, model.TypeName));
        }

        // PRAG0440: reachable, and nothing says who may reach it. Only when a step actually requires a
        // permission — a composite of steps that require none has nothing to suppress, and demanding a
        // declaration there would be noise rather than a guard.
        if (model is { IsExposed: true, DeclaresAuthorization: false, Location: not null }
            && !model.StepsRequiringPermission.IsDefaultOrEmpty)
        {
            var steps = model.StepsRequiringPermission.AsImmutableArray();
            context.ReportDiagnostic(Diagnostic.Create(
                ActionsDiagnostics.CompositeExposedWithoutPermission, model.Location,
                model.TypeName,
                steps.Length == 1 ? "" : "s",
                string.Join(", ", steps),
                steps.Length == 1 ? "s" : ""));
        }

        if (!model.Steps.IsDefaultOrEmpty || model.Location is null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            ActionsDiagnostics.CompositeHasNoSteps, model.Location, model.TypeName));
    }
}
