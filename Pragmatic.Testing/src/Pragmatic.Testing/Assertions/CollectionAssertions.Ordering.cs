namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Ordering assertions, split off because they need a comparer where the rest of the family
///     needs only equality.
/// </summary>
public sealed partial class CollectionAssertions<TItem>
{
    /// <summary>
    ///     Fails unless the sequence holds the same elements as <paramref name="expected"/>,
    ///     in any order.
    /// </summary>
    /// <remarks>
    ///     Elements are compared with <see cref="object.Equals(object)"/>, which is what the callers
    ///     rely on: these are sequences of strings, numbers and records, where equality is already
    ///     structural. A class without value equality would compare by reference here — as it does
    ///     everywhere else in the language.
    /// </remarks>
    public AndConstraint<CollectionAssertions<TItem>> BeEquivalentTo(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var other = expected as IReadOnlyList<TItem> ?? [.. expected];

        var missing = other.Where(e => _items.Count(i => Equals(i, e)) != other.Count(o => Equals(o, e))).ToList();
        var extra = _items.Where(i => other.Count(o => Equals(o, i)) != _items.Count(x => Equals(x, i))).ToList();

        if (_items.Count != other.Count || missing.Count > 0 || extra.Count > 0)
            AssertionFailure.Throw(Expression, $"to hold the same elements as {AssertionFailure.Format(other)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds the same elements as <paramref name="expected"/>, in any order.</summary>
    /// <remarks>
    ///     ⚠️ <b>This overload takes no reason.</b> On a <c>CollectionAssertions&lt;string&gt;</c> a call
    ///     like <c>BeEquivalentTo("x", "because y")</c> binds here, and the reason becomes a second
    ///     <i>expected element</i> — the assertion then fails naming something nobody wrote, which sends
    ///     the reader after the wrong thing. It cannot be resolved by overload: two strings are two
    ///     strings.
    ///     <para>
    ///         To give a reason, pass the expectation as a collection:
    ///         <c>BeEquivalentTo(["x"], "because y")</c> reaches the overload above.
    ///     </para>
    /// </remarks>
    public AndConstraint<CollectionAssertions<TItem>> BeEquivalentTo(params TItem[] expected) =>
        BeEquivalentTo(expected, null);

    /// <summary>Fails unless the sequence matches <paramref name="expected"/> under <paramref name="configure"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeEquivalentTo(
        IEnumerable<TItem> expected,
        Func<EquivalencyOptions, EquivalencyOptions> configure,
        string? because = null,
        params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return configure(new EquivalencyOptions()).StrictOrdering
            ? Equal(expected, because, becauseArgs)
            : BeEquivalentTo(expected, because, becauseArgs);
    }

    /// <summary>Fails when the sequence holds the same elements as <paramref name="unexpected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotBeEquivalentTo(
        IEnumerable<TItem> unexpected, string? because = null, params object[] becauseArgs)
    {
        var other = unexpected as IReadOnlyList<TItem> ?? [.. unexpected];
        var equivalent = _items.Count == other.Count
                         && other.All(e => _items.Count(i => Equals(i, e)) == other.Count(o => Equals(o, e)));

        if (equivalent)
            AssertionFailure.Throw(Expression, "not to hold the same elements", "it did", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless every element equals <paramref name="expected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> AllBeEquivalentTo(
        TItem expected, string? because = null, params object[] becauseArgs)
    {
        var offenders = _items.Where(i => !Equals(i, expected)).ToList();

        if (offenders.Count > 0)
            AssertionFailure.Throw(Expression, $"to hold only {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(offenders)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence begins with <paramref name="expected"/>.</summary>
    /// <remarks>Used on byte sequences, to assert a file's magic number without spelling out the rest.</remarks>
    public AndConstraint<CollectionAssertions<TItem>> StartWith(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var prefix = expected as IReadOnlyList<TItem> ?? [.. expected];

        if (_items.Count < prefix.Count || !_items.Take(prefix.Count).SequenceEqual(prefix))
            AssertionFailure.Throw(Expression, $"to start with {AssertionFailure.Format(prefix)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence begins with <paramref name="expected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> StartWith(TItem expected) => StartWith([expected], null);

    /// <summary>Fails unless the sequence ends with <paramref name="expected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> EndWith(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var suffix = expected as IReadOnlyList<TItem> ?? [.. expected];

        if (_items.Count < suffix.Count || !_items.Skip(_items.Count - suffix.Count).SequenceEqual(suffix))
            AssertionFailure.Throw(Expression, $"to end with {AssertionFailure.Format(suffix)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the elements are in ascending order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeInAscendingOrder(
        string? because = null, params object[] becauseArgs) =>
        BeOrdered(ascending: true, Comparer<TItem>.Default, because, becauseArgs);

    /// <summary>Fails unless the elements are in ascending order of <paramref name="selector"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeInAscendingOrder<TKey>(
        Func<TItem, TKey> selector, string? because = null, params object[] becauseArgs) =>
        BeOrdered(ascending: true, Comparer<TItem>.Create(
            (left, right) => Comparer<TKey>.Default.Compare(selector(left), selector(right))),
            because, becauseArgs);

    /// <summary>Fails unless the elements are in descending order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeInDescendingOrder(
        string? because = null, params object[] becauseArgs) =>
        BeOrdered(ascending: false, Comparer<TItem>.Default, because, becauseArgs);

    /// <summary>Fails unless the elements are in descending order of <paramref name="selector"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeInDescendingOrder<TKey>(
        Func<TItem, TKey> selector, string? because = null, params object[] becauseArgs) =>
        BeOrdered(ascending: false, Comparer<TItem>.Create(
            (left, right) => Comparer<TKey>.Default.Compare(selector(left), selector(right))),
            because, becauseArgs);

    /// <summary>
    ///     Names the first pair that is out of order, rather than saying only that the sequence is
    ///     unsorted — with a long collection, that is the difference between a usable failure and a
    ///     second debugging session.
    /// </summary>
    private AndConstraint<CollectionAssertions<TItem>> BeOrdered(
        bool ascending, IComparer<TItem> comparer, string? because, object[] becauseArgs)
    {
        var direction = ascending ? "ascending" : "descending";

        for (var i = 1; i < _items.Count; i++)
        {
            var comparison = comparer.Compare(_items[i - 1], _items[i]);
            if (ascending ? comparison <= 0 : comparison >= 0)
                continue;

            AssertionFailure.Throw(Expression, $"to be in {direction} order",
                $"{AssertionFailure.Format(_items[i - 1])} precedes {AssertionFailure.Format(_items[i])}",
                because, becauseArgs);
        }

        return Ok();
    }
}
