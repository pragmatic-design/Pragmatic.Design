namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A mocked method with more parameters than the typed arities carry. Configured and asserted
///     on through the boxed argument list.
/// </summary>
/// <remarks>
///     <para>
///         The typed <c>MockMethod</c> family stops at five parameters, and there is no honest number
///         at which to stop: generated domain actions take as many as their mutation has fields —
///         <c>IBillingActions.CreateDraftInvoice</c> takes seven. Adding arities chases a maximum that
///         does not exist, so past the last typed one a member becomes this instead of becoming
///         unusable.
///     </para>
///     <para>
///         What is lost is the compiler checking the arguments; what is kept is that every member of
///         every interface can be configured and asserted on. That trade is the right way round: a
///         member nobody can reach is worse than one reached without type inference.
///     </para>
/// </remarks>
public sealed class MockWideMethod : MockMember
{
    private readonly List<object?[]> _calls = [];
    private Func<object?[], object?>? _factory;
    private object? _result;
    private Exception? _throws;

    /// <param name="name">The member's display name.</param>
    /// <param name="defaultResult">What an unconfigured call returns, supplied by the generator.</param>
    public MockWideMethod(string? name = null, object? defaultResult = null) : base(name) =>
        _result = defaultResult;

    /// <summary>The arguments of every call, in order.</summary>
    public IReadOnlyList<object?[]> Calls => _calls;

    /// <summary>How many times the method was called.</summary>
    public int CallCount => _calls.Count;

    /// <summary>Configures the result returned by every call.</summary>
    public MockWideMethod Returns(object? result)
    {
        _result = result;
        _factory = null;
        return this;
    }

    /// <summary>Configures a result computed from the boxed arguments.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockWideMethod Returns(Func<object?[], object?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public MockWideMethod Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _throws = exception;
        return this;
    }

    /// <summary>Called by the generated explicit interface implementation.</summary>
    /// <typeparam name="TReturn">The method's return type.</typeparam>
    public TReturn Invoke<TReturn>(params object?[] arguments)
    {
        _calls.Add(arguments);

        if (_throws is not null)
            throw _throws;

        if (_factory is not null && _factory(arguments) is TReturn computed)
            return computed;

        return _result is TReturn configured ? configured : default!;
    }

    /// <summary>Called by the generated implementation of a wide method returning <see langword="void"/>.</summary>
    public void InvokeVoid(params object?[] arguments)
    {
        _calls.Add(arguments);

        if (_throws is not null)
            throw _throws;

        _factory?.Invoke(arguments);
    }

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertCallCount(times, _calls.Count, _calls.Count, null);

    /// <summary>Asserts the number of calls whose arguments satisfy <paramref name="where"/>.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times, Func<object?[], bool> where)
    {
        ArgumentNullException.ThrowIfNull(where);

        var matching = 0;
        foreach (var call in _calls)
            if (where(call))
                matching++;

        AssertCallCount(times, matching, _calls.Count, "the given argument predicate");
    }

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertCallCount(0, _calls.Count, _calls.Count, null);

    /// <summary>Forgets every recorded call, leaving the configuration in place.</summary>
    public void ClearReceivedCalls() => _calls.Clear();
}
