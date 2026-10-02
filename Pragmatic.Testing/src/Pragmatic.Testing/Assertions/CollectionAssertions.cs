namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions for a sequence of <typeparamref name="TItem"/>.</summary>
/// <remarks>
///     The sequence is materialised once, in the constructor. A caller passing a lazy query would
///     otherwise have it enumerated by every assertion in the chain — and a query with side effects,
///     or one over a mock, would count each enumeration as another call.
/// </remarks>
/// <typeparam name="TItem">The element type.</typeparam>
public sealed partial class CollectionAssertions<TItem>
    : SubjectAssertions<IEnumerable<TItem>?, CollectionAssertions<TItem>>
{
    private readonly IReadOnlyList<TItem> _items;

    internal CollectionAssertions(IEnumerable<TItem>? subject, string? expression)
        : base(subject, expression) =>
        _items = subject is null ? [] : [.. subject];

    /// <summary>
    ///     The materialised sequence — never null, and never re-enumerated.
    /// </summary>
    /// <remarks>
    ///     Hides the inherited <c>Subject</c>, which is the nullable sequence the caller passed in.
    ///     Callers reach for it to keep going after an assertion — <c>.And.Subject.First(…)</c> —
    ///     and a nullable there means a warning at every such site for a value the assertion has
    ///     already enumerated.
    /// </remarks>
    public new IReadOnlyList<TItem> Subject => _items;

    /// <summary>Fails unless the sequence holds exactly <paramref name="expected"/> elements.</summary>
    public AndConstraint<CollectionAssertions<TItem>> HaveCount(
        int expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || _items.Count != expected)
            AssertionFailure.Throw(Expression, $"to have {expected} element(s)",
                Subject is null ? "found <null>" : $"found {_items.Count}: {AssertionFailure.Format(_items)}",
                because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds more than <paramref name="expected"/> elements.</summary>
    public AndConstraint<CollectionAssertions<TItem>> HaveCountGreaterThan(
        int expected, string? because = null, params object[] becauseArgs)
    {
        if (_items.Count <= expected)
            AssertionFailure.Throw(Expression, $"to have more than {expected} element(s)",
                $"found {_items.Count}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds at least <paramref name="expected"/> elements.</summary>
    public AndConstraint<CollectionAssertions<TItem>> HaveCountGreaterOrEqualTo(
        int expected, string? because = null, params object[] becauseArgs)
    {
        if (_items.Count < expected)
            AssertionFailure.Throw(Expression, $"to have at least {expected} element(s)",
                $"found {_items.Count}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds fewer than <paramref name="expected"/> elements.</summary>
    public AndConstraint<CollectionAssertions<TItem>> HaveCountLessThan(
        int expected, string? because = null, params object[] becauseArgs)
    {
        if (_items.Count >= expected)
            AssertionFailure.Throw(Expression, $"to have fewer than {expected} element(s)",
                $"found {_items.Count}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence is empty.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (_items.Count != 0)
            AssertionFailure.Throw(Expression, "to be empty",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the sequence is empty.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotBeEmpty(string? because = null, params object[] becauseArgs)
    {
        if (_items.Count == 0)
            AssertionFailure.Throw(Expression, "not to be empty", "it was", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the sequence is null or empty.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotBeNullOrEmpty(
        string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || _items.Count == 0)
            AssertionFailure.Throw(Expression, "not to be null or empty",
                Subject is null ? "found <null>" : "it was empty", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence contains <paramref name="expected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> Contain(
        TItem expected, string? because = null, params object[] becauseArgs)
    {
        if (!_items.Contains(expected))
            AssertionFailure.Throw(Expression, $"to contain {AssertionFailure.Format(expected)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless some element satisfies <paramref name="predicate"/>.</summary>
    /// <remarks>The matched element is carried on the constraint, so <c>.Which</c> continues on it.</remarks>
    public AndWhichConstraint<CollectionAssertions<TItem>, TItem> Contain(
        Func<TItem, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matches = _items.Where(predicate).ToList();
        if (matches.Count == 0)
            AssertionFailure.Throw(Expression, "to contain an element matching the predicate",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return new AndWhichConstraint<CollectionAssertions<TItem>, TItem>(
            Self, matches.Count > 0 ? matches[0] : default!);
    }

    /// <summary>Fails unless the sequence contains every element of <paramref name="expected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> Contain(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var missing = expected.Where(e => !_items.Contains(e)).ToList();
        if (missing.Count > 0)
            AssertionFailure.Throw(Expression, $"to contain {AssertionFailure.Format(expected)}",
                $"it lacks {AssertionFailure.Format(missing)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the sequence contains <paramref name="unexpected"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotContain(
        TItem unexpected, string? because = null, params object[] becauseArgs)
    {
        if (_items.Contains(unexpected))
            AssertionFailure.Throw(Expression, $"not to contain {AssertionFailure.Format(unexpected)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when any element satisfies <paramref name="predicate"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotContain(
        Func<TItem, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (_items.Any(predicate))
            AssertionFailure.Throw(Expression, "not to contain an element matching the predicate",
                $"found {AssertionFailure.Format(_items.Where(predicate))}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds exactly one element.</summary>
    public AndWhichConstraint<CollectionAssertions<TItem>, TItem> ContainSingle(
        string? because = null, params object[] becauseArgs)
    {
        if (_items.Count != 1)
            AssertionFailure.Throw(Expression, "to contain a single element",
                $"found {_items.Count}: {AssertionFailure.Format(_items)}", because, becauseArgs);

        return new AndWhichConstraint<CollectionAssertions<TItem>, TItem>(
            Self, _items.Count == 1 ? _items[0] : default!);
    }

    /// <summary>Fails unless exactly one element satisfies <paramref name="predicate"/>.</summary>
    public AndWhichConstraint<CollectionAssertions<TItem>, TItem> ContainSingle(
        Func<TItem, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var matches = _items.Where(predicate).ToList();
        if (matches.Count != 1)
            AssertionFailure.Throw(Expression, "to contain a single element matching the predicate",
                $"found {matches.Count}", because, becauseArgs);

        return new AndWhichConstraint<CollectionAssertions<TItem>, TItem>(
            Self, matches.Count == 1 ? matches[0] : default!);
    }

    /// <summary>Fails unless the sequence holds the same elements, in the same order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> Equal(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var other = expected as IReadOnlyList<TItem> ?? [.. expected];

        if (!_items.SequenceEqual(other))
            AssertionFailure.Throw(Expression, $"to equal {AssertionFailure.Format(other)}",
                $"found {AssertionFailure.Format(_items)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the sequence holds the same elements, in the same order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> Equal(params TItem[] expected) => Equal(expected, null);

    /// <summary>Fails when the sequence holds the same elements in the same order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotEqual(
        IEnumerable<TItem> unexpected, string? because = null, params object[] becauseArgs)
    {
        if (_items.SequenceEqual(unexpected))
            AssertionFailure.Throw(Expression, "not to equal the given sequence", "it did", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless every element satisfies <paramref name="predicate"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> OnlyContain(
        Func<TItem, bool> predicate, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var offenders = _items.Where(i => !predicate(i)).ToList();
        if (offenders.Count > 0)
            AssertionFailure.Throw(Expression, "to contain only elements matching the predicate",
                $"{AssertionFailure.Format(offenders)} did not", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless every element is distinct.</summary>
    public AndConstraint<CollectionAssertions<TItem>> OnlyHaveUniqueItems(
        string? because = null, params object[] becauseArgs)
    {
        var duplicates = _items.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            AssertionFailure.Throw(Expression, "to hold only unique elements",
                $"{AssertionFailure.Format(duplicates)} appear more than once", because, becauseArgs);

        return Ok();
    }

    /// <summary>Runs <paramref name="assertion"/> against every element.</summary>
    public AndConstraint<CollectionAssertions<TItem>> AllSatisfy(
        Action<TItem> assertion, string? because = null, params object[] becauseArgs)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        foreach (var item in _items)
            assertion(item);

        return Ok();
    }

    /// <summary>Fails unless every element is exactly of type <typeparamref name="T"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> AllBeOfType<T>(
        string? because = null, params object[] becauseArgs)
    {
        var offenders = _items.Where(i => i?.GetType() != typeof(T)).ToList();
        if (offenders.Count > 0)
            AssertionFailure.Throw(Expression, $"to hold only {typeof(T).Name}",
                $"found {AssertionFailure.Format(offenders)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless every element of <paramref name="expected"/> is present, in order, contiguously.</summary>
    public AndConstraint<CollectionAssertions<TItem>> ContainInOrder(
        params TItem[] expected) => ContainInOrder(expected, null);

    /// <summary>Fails unless every element of <paramref name="expected"/> appears in that relative order.</summary>
    public AndConstraint<CollectionAssertions<TItem>> ContainInOrder(
        IEnumerable<TItem> expected, string? because = null, params object[] becauseArgs)
    {
        var index = 0;
        foreach (var wanted in expected)
        {
            var found = false;
            while (index < _items.Count)
                if (Equals(_items[index++], wanted))
                {
                    found = true;
                    break;
                }

            if (!found)
                AssertionFailure.Throw(Expression, "to contain the given elements in order",
                    $"found {AssertionFailure.Format(_items)}", because, becauseArgs);
        }

        return Ok();
    }

    /// <summary>Fails unless the sequence is a subset of <paramref name="superset"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> BeSubsetOf(
        IEnumerable<TItem> superset, string? because = null, params object[] becauseArgs)
    {
        var other = superset as ICollection<TItem> ?? [.. superset];
        var extra = _items.Where(i => !other.Contains(i)).ToList();

        if (extra.Count > 0)
            AssertionFailure.Throw(Expression, "to be a subset",
                $"it also holds {AssertionFailure.Format(extra)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the sequence shares any element with <paramref name="other"/>.</summary>
    public AndConstraint<CollectionAssertions<TItem>> NotIntersectWith(
        IEnumerable<TItem> other, string? because = null, params object[] becauseArgs)
    {
        var shared = _items.Intersect(other).ToList();
        if (shared.Count > 0)
            AssertionFailure.Throw(Expression, "not to intersect",
                $"both hold {AssertionFailure.Format(shared)}", because, becauseArgs);

        return Ok();
    }
}
