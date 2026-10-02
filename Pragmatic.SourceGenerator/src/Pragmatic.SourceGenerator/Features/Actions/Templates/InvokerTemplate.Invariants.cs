using System.Linq;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

internal sealed partial class InvokerTemplate
{
    /// <summary>
    ///     Renders the <c>CheckLoadedInvariants</c> override: the rules of each loaded entity that this
    ///     operation can answer, in declaration order, first violation refusing the operation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An <c>[Invariant]</c> checked by the mutation invoker and nowhere else would leave an
    ///         aggregate moved by a <c>[DomainAction]</c> — which is how a state machine is moved — with
    ///         its rules evaluated on no path at all.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>CheckableInvariants</c> is already filtered by the transform against the
    ///         operation's <c>Include</c> list, and this template must not second-guess it: a rule over a
    ///         navigation nobody loaded reads an empty collection, and refusing a correct write is worse
    ///         than not checking. The model answers the question once.
    ///     </para>
    ///     <para>
    ///         The refusal is the same <c>InvariantViolationError</c> the mutation path returns, message
    ///         key included, so one rule reads the same in the caller's language whichever operation
    ///         broke it.
    ///     </para>
    /// </remarks>
    private void RenderCheckLoadedInvariants()
    {
        XmlSummary(
            "Checks the [Invariant] rules of the loaded entities this operation can answer — those "
            + "reading no navigation outside its Include list — after the body and before the write.");
        AppendLine(
            "protected override global::Pragmatic.Result.IError? "
            + $"CheckLoadedInvariants({_model.TypeName} action)");
        Block(() =>
        {
            foreach (var load in _model.LoadEntities.Where(l => l.CheckableInvariants.Count > 0))
            {
                // A collection load is a list, and a rule of the entity is about one row: asking it of
                // a list would not compile. Only the single-row loads carry rules here.
                if (load.IsMany || load.ExistsOnly)
                    continue;

                Comment($"{load.EntityTypeShortName}, loaded by this operation.");

                foreach (var invariant in load.CheckableInvariants)
                {
                    AppendLine($"if (!action.{load.FieldName}.{invariant.MethodName}())");
                    IncreaseIndent();
                    AppendLine(Refusal(invariant.MethodName, invariant));
                    DecreaseIndent();
                }

                AppendLine();
            }

            AppendLine("return null;");
        });
    }

    /// <summary>The refusal one broken rule returns — the mutation path's, so the two cannot differ.</summary>
    private static string Refusal(string name, Models.InvariantModel invariant)
    {
        var message = StringHelper.CSharpLiteral(invariant.Message ?? "");
        var key = invariant.MessageKey is { Length: > 0 } declared
            ? $", \"{StringHelper.CSharpLiteral(declared)}\""
            : "";

        return "return new global::Pragmatic.Actions.Mutation.InvariantViolationError("
               + $"\"{name}\", \"{message}\"{key});";
    }
}
