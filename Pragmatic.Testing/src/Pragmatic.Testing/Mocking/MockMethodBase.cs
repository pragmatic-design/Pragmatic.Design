namespace Pragmatic.Testing.Mocking;

/// <summary>
///     What every mocked method shares regardless of arity: the recorded calls, the configured
///     failure, and the counting behind <c>Received</c>.
/// </summary>
/// <typeparam name="TArgs">
///     The recorded argument shape — the single argument for a one-parameter method, a tuple beyond
///     that, and <see cref="ValueTuple"/> for a parameterless one.
/// </typeparam>
public abstract class MockMethodBase<TArgs> : MockMember
{
    private readonly List<TArgs> _calls = [];

    /// <param name="name">The member's display name, e.g. <c>ICacheStack.RemoveAsync</c>.</param>
    protected MockMethodBase(string? name = null) : base(name)
    {
    }

    /// <summary>The arguments of every call, in order.</summary>
    public IReadOnlyList<TArgs> Calls => _calls;

    /// <summary>How many times the method was called, with any arguments.</summary>
    public int CallCount => _calls.Count;

    /// <summary>The exception the method throws when configured to, otherwise null.</summary>
    protected Exception? ConfiguredException { get; private set; }

    /// <summary>Configures the method to throw when called.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    protected void SetThrows(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ConfiguredException = exception;
    }

    /// <summary>Records a call and applies a configured exception, if any.</summary>
    protected void Record(TArgs args)
    {
        _calls.Add(args);
        if (ConfiguredException is not null)
            throw ConfiguredException;
    }

    /// <summary>
    ///     Counts the calls satisfying <paramref name="predicate"/> and asserts the total.
    ///     A null predicate counts every call.
    /// </summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    protected void AssertReceived(int times, Func<TArgs, bool>? predicate, string? constraint)
    {
        if (predicate is null)
        {
            AssertCallCount(times, _calls.Count, _calls.Count, null);
            return;
        }

        var matching = 0;
        foreach (var call in _calls)
            if (predicate(call))
                matching++;

        AssertCallCount(times, matching, _calls.Count, constraint);
    }

    /// <summary>Forgets every recorded call, leaving the configured behaviour in place.</summary>
    /// <remarks>
    ///     For the arrange-act-assert shape where the arrangement itself calls the mock and those
    ///     calls would otherwise be counted by the assertion.
    /// </remarks>
    public void ClearReceivedCalls() => _calls.Clear();
}
