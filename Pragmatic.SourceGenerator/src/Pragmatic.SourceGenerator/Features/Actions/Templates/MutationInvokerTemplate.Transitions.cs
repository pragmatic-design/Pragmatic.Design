using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary><c>[TransitionsTo]</c> on a mutation: the invoker moves the entity, or checks the body did.</summary>
internal sealed partial class MutationInvokerTemplate
{
    private void RenderTransition()
    {
        if (_model.Transition is not { } transition)
            return;

        var entityType = _model.EntityFullTypeName;

        if (transition.InvokerTransitions)
        {
            var hook = transition.Timing == TransitionTimingValue.AfterBody
                ? "TransitionAfterBody"
                : "TransitionBeforeBody";

            AppendLine();
            XmlSummary($"[TransitionsTo] {transition.TargetMember}: the move this mutation declares.");
            AppendLine($"protected override global::Pragmatic.Result.IError? {hook}({entityType} entity)");
            Block(() =>
            {
                foreach (var line in TransitionEmitter.PerformLines(transition, "entity"))
                    AppendLine(line);
            });
        }
        else if (transition.InvokerChecksTheBody)
        {
            AppendLine();
            XmlSummary($"[TransitionsTo] {transition.TargetMember} with ByBody: the body must have moved the entity.");
            AppendLine($"protected override void EnsureTheBodyTransitioned({entityType} entity)");
            Block(() =>
            {
                foreach (var line in TransitionEmitter.CheckLines(transition, "entity", _model.TypeName))
                    AppendLine(line);
            });
        }
    }
}
