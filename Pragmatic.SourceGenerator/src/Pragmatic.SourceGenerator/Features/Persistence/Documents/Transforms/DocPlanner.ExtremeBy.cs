using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     <c>MaxByAsync(o => o.Total)</c> and <c>MinByAsync</c>: the chain ordered by the key (descending for MaxBy) in place of its ordering, then
///     by the document's key, so documents that tie give the same one on every run, and read as <c>FirstOrDefaultAsync</c> reads it.
/// </summary>
internal sealed partial class DocPlanner
{
    private void ExtremeBy(string name, LambdaExpressionSyntax? lambda)
    {
        if (SelectSlot >= 0)
        {
            Fail(_call.Invocation, $"{name} reads the documents themselves: put it before a Select");
            return;
        }

        if (_call.Paged || _call.Before.Exists(s => s.Name == "After"))
        {
            Fail(_call.Invocation, $"{name} orders the documents the chain selects and reads the first: a Skip or a Take before it would page the documents in another order, and a cursor reads after another ordering");
            return;
        }

        if (OrderingSlot >= 0)
        {
            Fail(_call.Invocation, $"{name} orders the documents by its key: an ordering composed at runtime (OrderedBy) before it is not compiled");
            return;
        }

        if (_entity.Key is not { } key)
        {
            Fail(_call.Invocation, $"{name} breaks a tie by the document's key: {_entity.Type.Name} has none");
            return;
        }

        var path = KeyPath(lambda, _call.Invocation);
        if (path is null)
            return;
        _sort.Clear(); // in place of the chain's OrderBy, as an OrderBy replaces the one before it
        _sort.Add((path, name == "MaxByAsync"));
        if (path != key.Name)
            _sort.Add((key.Name, false));
    }
}
