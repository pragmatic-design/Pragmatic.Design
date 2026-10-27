using System;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A value an update computes from the document's own fields (<c>o =&gt; o.Stars + 1</c>): the aggregation expression MongoDB evaluates on
///     the server, converted to the field's type, and the C# code over <c>e</c>, the document as read, that Cosmos DB's patch is written from.
/// </summary>
/// <param name="write">Statements writing the expression under the element its argument names (a literal: a name, or an array's index).</param>
/// <param name="converter">The operator that keeps the field's type (<c>$toInt</c>, <c>$toLong</c>, <c>$toDouble</c>, <c>$toDecimal</c>).</param>
/// <param name="code">The value in C#, over <c>e</c>.</param>
internal sealed class DocComputed(Func<string, string> write, string? converter, string code)
{
    public string Code { get; } = code;

    /// <summary>The field set to the expression: under <paramref name="name" />, through the converter when the field's type needs one.</summary>
    public string Mongo(string name)
        => converter is null ? write(name) : $"w.StartDocument({name}); {write(DocTypeEmitter.Literal(converter))}w.EndDocument(); ";
}
