using Microsoft.CodeAnalysis;

namespace Pragmatic.Persistence.Documents.Generator;

/// <summary>
///     <c>SelectMany(o => o.Lines)</c>: the elements of an embedded list, one result per element, after the chain's filter and ordering of the
///     documents (MongoDB: <c>$unwind</c> and <c>$replaceRoot</c>; Cosmos DB: <c>JOIN u IN c.Lines</c>). Then Skip, Take and ToListAsync.
/// </summary>
internal sealed partial class DocPlanner
{
    /// <summary>The embedded list a SelectMany flattens; null for every other chain.</summary>
    private string? _unwind;

    /// <summary>True when the chain flattens an embedded list: its terminal reads the elements.</summary>
    public bool Unwound => _unwind is not null;

    private void Unwind(DocStep step)
    {
        if (_unwind is not null || _groupKey is not null || SelectSlot >= 0)
        {
            Fail(step.Invocation, "one SelectMany, before any Select or GroupBy");
            return;
        }

        var parameters = Parameters(step.Lambda);
        if (step.Lambda?.Body is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax body && parameters.Count == 1)
        {
            var translator = new DocTranslator(_call.Model, _entity, _models, parameters[0], null, null, _diagnostics, step.Invocation.GetLocation());
            if (translator.Path(body, out var field) is { } path && field is { Kind: DocKind.List, Embedded: not null })
            {
                _unwind = path;
                return;
            }
        }

        Fail(step.Invocation, "SelectMany takes a list of [Embedded] documents of the document: o => o.Lines");
    }

    private string UnwindInitializer => _unwind is null ? "null" : DocTypeEmitter.Literal(_unwind);
}
