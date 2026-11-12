using System.Linq;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed partial class InvokerTemplate
{
    /// <summary>Whether an entity this operation loads validates its own fields: the invoker then validates it after the body.</summary>
    private bool ValidatesLoadedEntities => _model.LoadEntities.Any(load => load is { HasSyncValidator: true, ExistsOnly: false });

    /// <summary>
    ///     Renders the <c>ValidateLoadedEntities</c> override: the field rules of each loaded entity the body changed, first failure refusing
    ///     the operation (#628).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The mutation invoker validates the entity it writes; an action that loads one and sets a field wrote it with no rule asked.
    ///         The entity's generated <c>Validate()</c> is called through <c>ISyncValidator</c>, which <c>HasSyncValidator</c> predicts it
    ///         implements, so a wrong prediction is a compile error here rather than a validation that never runs.
    ///     </para>
    ///     <para>
    ///         "Changed" is asked of the boundary's unit of work. An operation with no boundary has none to ask, and validates every entity it
    ///         loaded: refusing a row whose stored value already breaks a rule is the smaller fault next to writing one no rule was asked of.
    ///     </para>
    /// </remarks>
    private void RenderValidateLoadedEntities()
    {
        XmlSummary("Validates the field rules of each loaded entity the body changed, after the body and before the write.");
        AppendLine(
            "protected override global::Pragmatic.Result.IError? "
            + $"ValidateLoadedEntities({_model.TypeName} action)");
        Block(() =>
        {
            foreach (var load in _model.LoadEntities.Where(l => l is { HasSyncValidator: true, ExistsOnly: false }))
            {
                if (load.IsMany)
                {
                    Comment($"Every {load.EntityTypeShortName} of the list this operation loaded, row by row.");
                    AppendLine($"foreach (var __row in action.{load.FieldName})");
                    IncreaseIndent();
                    AppendLine($"if ({Changed("__row")}((global::Pragmatic.Validation.ISyncValidator)__row).Validate() is {{ IsFailure: true }} __rowError)");
                    IncreaseIndent();
                    AppendLine("return __rowError;");
                    DecreaseIndent();
                    DecreaseIndent();
                    AppendLine();
                    continue;
                }

                // Named after the field, not the type: an operation may load two rows of one type (a pair), and the field is unique.
                var local = "__" + load.FieldName.TrimStart('_');
                Comment($"{load.EntityTypeShortName}, loaded by this operation.");
                AppendLine($"if (action.{load.FieldName} is {{ }} {local} && {Changed(local)}"
                           + $"((global::Pragmatic.Validation.ISyncValidator){local}).Validate() is {{ IsFailure: true }} {local}Error)");
                IncreaseIndent();
                AppendLine($"return {local}Error;");
                DecreaseIndent();
                AppendLine();
            }

            AppendLine("return null;");
        });
    }

    /// <summary>The "the body changed it" half of the condition, ending in <c>&amp;&amp;</c>; empty when there is no unit of work to ask.</summary>
    private string Changed(string entity) => HasBoundary ? $"_unitOfWork.IsModified({entity}) && " : "";
}
