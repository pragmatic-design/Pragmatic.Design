namespace Pragmatic.Testing.Assertions;

/// <summary>Assertions for a keyed collection.</summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class DictionaryAssertions<TKey, TValue>
    : SubjectAssertions<IReadOnlyDictionary<TKey, TValue>?, DictionaryAssertions<TKey, TValue>>
    where TKey : notnull
{
    internal DictionaryAssertions(IReadOnlyDictionary<TKey, TValue>? subject, string? expression)
        : base(subject, expression) { }

    /// <summary>Fails unless the dictionary holds <paramref name="key"/>.</summary>
    /// <remarks>The value is carried on the constraint, so <c>.Which</c> continues on it.</remarks>
    public AndWhichConstraint<DictionaryAssertions<TKey, TValue>, TValue> ContainKey(
        TKey key, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.TryGetValue(key, out var value))
        {
            AssertionFailure.Throw(Expression, $"to contain key {AssertionFailure.Format(key)}",
                Subject is null ? "found <null>" : $"found keys {AssertionFailure.Format(Subject.Keys)}",
                because, becauseArgs);
            value = default!;
        }

        return new AndWhichConstraint<DictionaryAssertions<TKey, TValue>, TValue>(Self, value);
    }

    /// <summary>Fails unless the dictionary holds every one of <paramref name="keys"/>.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> ContainKeys(params TKey[] keys) =>
        ContainKeys(keys, null);

    /// <summary>Fails unless the dictionary holds every one of <paramref name="keys"/>.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> ContainKeys(
        IEnumerable<TKey> keys, string? because = null, params object[] becauseArgs)
    {
        var missing = keys.Where(k => Subject is null || !Subject.ContainsKey(k)).ToList();

        if (missing.Count > 0)
            AssertionFailure.Throw(Expression, "to contain the given keys",
                $"it lacks {AssertionFailure.Format(missing)}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the dictionary holds <paramref name="key"/>.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> NotContainKey(
        TKey key, string? because = null, params object[] becauseArgs)
    {
        if (Subject is not null && Subject.ContainsKey(key))
            AssertionFailure.Throw(Expression, $"not to contain key {AssertionFailure.Format(key)}",
                "it did", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the dictionary maps <paramref name="key"/> to <paramref name="value"/>.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> Contain(
        TKey key, TValue value, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || !Subject.TryGetValue(key, out var actual) || !Equals(actual, value))
            AssertionFailure.Throw(Expression,
                $"to map {AssertionFailure.Format(key)} to {AssertionFailure.Format(value)}",
                Subject is null || !Subject.ContainsKey(key)
                    ? "the key is absent"
                    : $"found {AssertionFailure.Format(Subject[key])}",
                because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the dictionary holds exactly <paramref name="expected"/> entries.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> HaveCount(
        int expected, string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || Subject.Count != expected)
            AssertionFailure.Throw(Expression, $"to have {expected} entries",
                Subject is null ? "found <null>" : $"found {Subject.Count}", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails unless the dictionary holds exactly one entry.</summary>
    public AndWhichConstraint<DictionaryAssertions<TKey, TValue>, KeyValuePair<TKey, TValue>> ContainSingle(
        string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || Subject.Count != 1)
            AssertionFailure.Throw(Expression, "to hold a single entry",
                Subject is null ? "found <null>" : $"found {Subject.Count}", because, becauseArgs);

        return new AndWhichConstraint<DictionaryAssertions<TKey, TValue>, KeyValuePair<TKey, TValue>>(
            Self, Subject is { Count: 1 } ? Subject.First() : default);
    }

    /// <summary>Fails unless the dictionary is empty.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> BeEmpty(
        string? because = null, params object[] becauseArgs)
    {
        if (Subject is { Count: > 0 })
            AssertionFailure.Throw(Expression, "to be empty",
                $"found {Subject.Count} entries", because, becauseArgs);

        return Ok();
    }

    /// <summary>Fails when the dictionary is empty.</summary>
    public AndConstraint<DictionaryAssertions<TKey, TValue>> NotBeEmpty(
        string? because = null, params object[] becauseArgs)
    {
        if (Subject is null || Subject.Count == 0)
            AssertionFailure.Throw(Expression, "not to be empty",
                Subject is null ? "found <null>" : "it was", because, becauseArgs);

        return Ok();
    }
}
