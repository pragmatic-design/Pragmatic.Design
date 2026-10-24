namespace Pragmatic.Persistence.Documents;

/// <summary>
///     The fields an <c>ExecuteUpdateAsync</c> sets, each <see cref="Set{TValue}" /> a field of the document and its value (a constant, or the
///     two-parameter form's argument): read by the generator into the update's <c>$set</c>; the calls never run.
/// </summary>
public sealed class DocSetters<T>
{
    private DocSetters()
    {
    }

    public DocSetters<T> Set<TValue>(Func<T, TValue> field, TValue value)
        => throw new NotSupportedException("DocSetters.Set is read at compile time, inside ExecuteUpdateAsync: it never runs.");
}
