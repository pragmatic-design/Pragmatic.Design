namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The mutation's preparation hook: its bound properties written, the signed-in user's entity and the
///     entities <c>[LoadEntity]</c> declares besides its own row loaded the way an action loads them —
///     after authorization, before the row is loaded or created, 404 for a key that names nothing.
/// </summary>
/// <remarks>
///     Before the row, so the generated mapping carries a bound value into the entity like any other
///     property, and <c>ApplyAsync</c> reads it already written.
/// </remarks>
internal sealed partial class MutationInvokerTemplate
{
    private void RenderPrepareMutationAsync()
    {
        var hook = new PreparationHook(_model.Bindings, _model.HasLoadEntities, _model.LoadedValidation, _model.LoadFromQueries);
        if (!hook.IsNeeded)
            return;

        XmlSummary("Writes the [FromClock] and [FromCurrentUser] properties and loads the signed-in user and the [LoadEntity] entities before the mutation runs.");
        AppendLine(
            $"protected override {hook.ReturnType} PrepareMutationAsync({_model.FullTypeName} mutation, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            foreach (var statement in hook.BindingStatements("mutation", "_serviceProvider"))
                AppendLine(statement);

            foreach (var statement in LoadEntityTemplate.PermissionStatements("_serviceProvider", _model.FullTypeName, _model.LoadEntities))
                AppendLine(statement);

            foreach (var statement in hook.QueryStatements("mutation", "_serviceProvider"))
                AppendLine(statement);

            // The same scopes LoadEntityAsync opens for the mutation's own row: an entity the mutation
            // declares it reads past a filter is preloaded past it too.
            if (hook.Loads && _model.HasFilterOverrides)
                RenderFilterOverrideScopes();

            foreach (var statement in hook.CurrentUserLoadStatements("mutation", "_serviceProvider"))
                AppendLine(statement);

            if (_model.HasLoadEntities)
                foreach (var statement in LoadEntityTemplate.PreparationStatements("mutation", _model.LoadEntities))
                    AppendLine(statement);

            // The rules on what was just loaded: validation after the load, not a second read in a
            // validator that cannot see the operation's fields.
            foreach (var statement in hook.LoadedValidationStatements("mutation"))
                AppendLine(statement);

            AppendLine(hook.Success);
        });
        AppendLine();
    }
}
