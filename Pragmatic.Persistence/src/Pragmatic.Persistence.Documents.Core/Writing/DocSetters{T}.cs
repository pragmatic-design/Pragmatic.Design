namespace Pragmatic.Persistence.Documents.Writing;

/// <summary>
///     The fields an <c>ExecuteUpdateAsync</c> sets, each <see cref="Set{TValue}(Func{T, TValue}, TValue)" /> a field of the document and its value
///     (a constant, or the two-parameter form's argument), each <see cref="Set{TValue}(Func{T, TValue}, Func{T, TValue})" /> a field and a value
///     computed from the document's own fields: read by the generator into the update's <c>$set</c>; the calls never run.
/// </summary>
public sealed class DocSetters<T>
{
    private DocSetters()
    {
    }

    public DocSetters<T> Set<TValue>(Func<T, TValue> field, TValue value)
        => throw new NotSupportedException("DocSetters.Set is read at compile time, inside ExecuteUpdateAsync: it never runs.");

    /// <summary>
    ///     The field set to a value computed from the document's fields with + - * / % and constants (<c>o =&gt; o.Stars + 1</c>): on the server on
    ///     MongoDB (an update pipeline), from the document as read on Cosmos DB.
    /// </summary>
    public DocSetters<T> Set<TValue>(Func<T, TValue> field, Func<T, TValue> computed)
        => throw new NotSupportedException("DocSetters.Set is read at compile time, inside ExecuteUpdateAsync: it never runs.");
}
