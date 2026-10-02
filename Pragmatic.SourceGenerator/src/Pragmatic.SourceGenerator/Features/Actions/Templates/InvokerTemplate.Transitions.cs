namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     <c>[TransitionsTo]</c> on a domain action: the invoker moves the <c>[LoadEntity]</c> row before the
///     body, or checks the body did.
/// </summary>
internal sealed partial class InvokerTemplate
{
    private void RenderTransition()
    {
        if (_model.Transition is not { EntityFieldName: { } field } transition)
            return;

        // Nested in the action, so the loaded field is reachable although the action declares it private.
        var entity = $"action.{field}";

        if (transition.InvokerTransitions)
        {
            AppendLine();
            XmlSummary($"[TransitionsTo] {transition.TargetMember}: the move this action declares, before its body.");
            AppendLine($"protected override global::Pragmatic.Result.IError? TransitionBeforeBody({_model.FullTypeName} action)");
            Block(() =>
            {
                foreach (var line in TransitionEmitter.PerformLines(transition, entity))
                    AppendLine(line);
            });
        }
        else if (transition.InvokerChecksTheBody)
        {
            AppendLine();
            XmlSummary($"[TransitionsTo] {transition.TargetMember} with ByBody: the body must have moved the entity.");
            AppendLine($"protected override void EnsureTheBodyTransitioned({_model.FullTypeName} action)");
            Block(() =>
            {
                foreach (var line in TransitionEmitter.CheckLines(transition, entity, _model.TypeName))
                    AppendLine(line);
            });
        }
    }
}
