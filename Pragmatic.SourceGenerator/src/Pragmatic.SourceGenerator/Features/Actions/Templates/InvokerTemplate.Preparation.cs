namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     The action's preparation hook: its bound properties written, the signed-in user's entity and its
///     <c>[LoadEntity]</c> entities loaded, after the filters and before <c>Execute</c>.
/// </summary>
internal sealed partial class InvokerTemplate
{
    private void RenderPrepareActionAsync()
    {
        var hook = new PreparationHook(_model.Bindings, _model.HasLoadEntities, _model.LoadedValidation, _model.LoadFromQueries);

        XmlSummary("Writes the [FromClock] and [FromCurrentUser] properties and loads the signed-in user and the [LoadEntity] entities before action execution.");
        AppendLine($"protected override {hook.ReturnType} PrepareActionAsync({_model.TypeName} action, global::System.Threading.CancellationToken ct)");
        Block(() =>
        {
            foreach (var statement in hook.BindingStatements("action", "ServiceProvider"))
                AppendLine(statement);

            foreach (var statement in LoadEntityTemplate.PermissionStatements("ServiceProvider", _model.TypeName, _model.LoadEntities))
                AppendLine(statement);

            foreach (var statement in hook.QueryStatements("action", "ServiceProvider"))
                AppendLine(statement);

            // The loads run before ExecuteActionAsync, where the scopes of [WithoutFilter]/[FilterMode]
            // open: without their own, they read the rows the operation declared it reads past a filter
            // with the filter on — 404 for a soft-deleted employee the erasure exists for.
            if (hook.Loads && _model.HasFilterOverrides)
                RenderFilterOverrideScopes();

            foreach (var statement in hook.CurrentUserLoadStatements("action", "ServiceProvider"))
                AppendLine(statement);

            if (_model.HasLoadEntities)
                foreach (var statement in LoadEntityTemplate.PreparationStatements("action", _model.LoadEntities))
                    AppendLine(statement);

            // The rules on what was just loaded: validation after the load, not a second read in a
            // validator that cannot see the operation's fields.
            foreach (var statement in hook.LoadedValidationStatements("action"))
                AppendLine(statement);

            AppendLine(hook.Success);
        });
    }
}
