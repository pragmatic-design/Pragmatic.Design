using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     <c>Join(session.Query&lt;TInner&gt;().Where(…), o => o.Key, i => i.Key, (o, i) => …)</c>: the inner chain's conditions (its own slots, read from
///     the inner chain stored in this one's), the two key paths, and the Cosmos DB query of the inner documents of a set of keys. Written as the
///     <c>DocJoin&lt;TInner&gt;</c> initializer beside the plan; the execution joins on the server (MongoDB) or here (Cosmos DB).
/// </summary>
internal sealed partial class DocPlanner
{
    /// <summary>The <c>DocJoin&lt;TInner&gt;</c> initializer; null for every other chain.</summary>
    public string? JoinInitializer { get; private set; }

    /// <summary>The inner document type and the key type, fully qualified, for the terminal's call.</summary>
    public string? JoinInner { get; private set; }

    public string? JoinKey { get; private set; }

    private void Join(DocStep step)
    {
        if (JoinInitializer is not null || _unwind is not null || _groupKey is not null || SelectSlot >= 0)
        {
            Fail(step.Invocation, "one Join, before any Select, SelectMany or GroupBy");
            return;
        }

        var arguments = step.Invocation.ArgumentList.Arguments;
        var innerType = step.Method.TypeArguments[0] as INamedTypeSymbol;
        if (arguments.Count != 4 || innerType is null || _models(innerType) is not { IsEmbedded: false } inner)
        {
            Fail(step.Invocation, "a Join takes another collection's chain (session.Query<TInner>() and its Wheres), the two keys and the result");
            return;
        }

        // The inner chain: session.Query<TInner>() and Wheres only, their values in the inner chain's own slots.
        var steps = new List<DocStep>();
        if (DocCall.StepsOf(arguments[0].Expression, _call.Model, steps) is null || steps.Any(s => s.Name != "Where"))
        {
            Fail(arguments[0].Expression, "the inner chain of a Join is session.Query<TInner>() with Wheres, written in place");
            return;
        }

        var conditions = new List<DocCondition>();
        var innerSlot = 0;
        foreach (var where in steps)
        {
            Condition(where.Lambda, where, where.TakesArg ? innerSlot : -1, inner, conditions);
            if (where.TakesArg)
                innerSlot++;
        }

        var local = KeyPath(arguments[1].Expression as LambdaExpressionSyntax, step.Invocation);
        var foreign = KeyPath(arguments[2].Expression as LambdaExpressionSyntax, step.Invocation, inner);
        if (local is null || foreign is null || _failed)
            return;

        var filter = DocCondition.All(conditions);
        var writer = new StringBuilder();
        if (filter is null)
        {
            writer.Append("null");
        }
        else
        {
            writer.AppendLine("static (w, s) =>");
            writer.AppendLine("            {");
            filter.Render(writer, "                ");
            writer.Append("            }");
        }

        // Cosmos DB: the inner documents whose key is among the outer keys (one array parameter), and the inner chain's conditions.
        var context = new DocCosmosText();
        var text = "SELECT * FROM c WHERE ARRAY_CONTAINS(@__keys, " + DocCosmosText.Path(foreign, "c") + ")" + (filter is null ? "" : " AND " + filter.RenderCosmos(context, "c"));
        var parameters = context.HasParameters ? $"static (w, s) => {{ {context.Parameters}}}" : "null";
        JoinInner = innerType.ToDisplayString(DocModel.Qualified);
        JoinKey = step.Method.TypeArguments[1].ToDisplayString(DocModel.Qualified);
        JoinInitializer = $"new global::Pragmatic.Persistence.Documents.DocJoin<{JoinInner}>({writer}, {DocTypeEmitter.Literal(local)}, {DocTypeEmitter.Literal(foreign)}, {DocTypeEmitter.Literal(text)}, {parameters}, {_slot})";
        _slot += 4; // the inner chain, the two keys, the result: the interceptor's order
    }
}
