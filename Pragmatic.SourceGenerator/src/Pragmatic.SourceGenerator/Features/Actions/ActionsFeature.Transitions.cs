using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Diagnostics;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions;

internal static partial class ActionsFeature
{
    /// <summary>PRAG0465-0468: a <c>[TransitionsTo]</c> the invoker cannot perform as declared.</summary>
    /// <remarks>Reported on the attribute, where the declaration to fix is.</remarks>
    private static void ReportTransitionProblem(
        SourceProductionContext context, string typeName, Location? fallback, TransitionModel? transition)
    {
        if (transition is null || transition.Problem == TransitionProblem.None)
            return;

        var location = transition.AttributeLocation?.ToLocation() ?? fallback;
        var enumName = ShortName(transition.EnumFullTypeName);
        var target = $"{enumName}.{transition.TargetMember}";

        var diagnostic = transition.Problem switch
        {
            TransitionProblem.NoEntityWithThatStateMachine => Diagnostic.Create(
                ActionsDiagnostics.TransitionHasNoEntity, location, typeName, enumName,
                transition.EntityTypeName.Length > 0
                    ? $"'{transition.EntityTypeName}' has no [StateMachine<{enumName}>]"
                    : $"no entity loaded with [LoadEntity] has a [StateMachine<{enumName}>]"),
            TransitionProblem.AmbiguousEntity => Diagnostic.Create(
                ActionsDiagnostics.TransitionHasNoEntity, location, typeName, enumName,
                $"more than one loaded entity has one ({transition.ProblemDetail})"),
            TransitionProblem.BodyAlsoTransitions => Diagnostic.Create(
                ActionsDiagnostics.BodyAlsoTransitions, location, typeName, target),
            TransitionProblem.AfterBodyOnAnAction => Diagnostic.Create(
                ActionsDiagnostics.AfterBodyOnAnAction, location, typeName),
            TransitionProblem.MutationIsNotAnUpdate => Diagnostic.Create(
                ActionsDiagnostics.TransitionOnAMutationThatIsNotAnUpdate, location, typeName),
            _ => null
        };

        if (diagnostic is not null)
            context.ReportDiagnostic(diagnostic);
    }
}
