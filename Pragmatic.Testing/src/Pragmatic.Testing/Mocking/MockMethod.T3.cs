namespace Pragmatic.Testing.Mocking;

/// <summary>A mocked three-parameter method returning <typeparamref name="TResult"/>.</summary>
/// <typeparam name="T1">The first parameter type.</typeparam>
/// <typeparam name="T2">The second parameter type.</typeparam>
/// <typeparam name="T3">The third parameter type.</typeparam>
/// <typeparam name="TResult">The return type.</typeparam>
public sealed class MockMethod<T1, T2, T3, TResult> : MockMethodBase<(T1 Arg1, T2 Arg2, T3 Arg3)>
{
    private readonly List<(Func<(T1, T2, T3), bool> Match, Func<(T1, T2, T3), TResult> Factory)> _rules = [];
    private Func<T1, T2, T3, TResult>? _factory;

    /// <param name="name">The member's display name.</param>
    public MockMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Configures the value returned for any arguments.</summary>
    public MockMethod<T1, T2, T3, TResult> Returns(TResult value)
    {
        _factory = (_, _, _) => value;
        return this;
    }

    /// <summary>Configures a factory over the arguments, evaluated on every call.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockMethod<T1, T2, T3, TResult> Returns(Func<T1, T2, T3, TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockMethod<T1, T2, T3, TResult> Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>
    ///     Configures a result for calls whose arguments match. Omitted matchers match anything.
    /// </summary>
    public MockMethodSetup<(T1, T2, T3), TResult> When(ArgMatcher<T1> arg1,
        ArgMatcher<T2> arg2 = default, ArgMatcher<T3> arg3 = default) =>
        new(call => arg1.Matches(call.Item1) && arg2.Matches(call.Item2) && arg3.Matches(call.Item3),
            (match, factory) => _rules.Add((match, factory)));

    /// <summary>Called by the generated explicit interface implementation.</summary>
    public TResult Invoke(T1 arg1, T2 arg2, T3 arg3)
    {
        Record((arg1, arg2, arg3));

        // Most recent matching rule wins — see MockMethod<T1, TResult>.
        for (var i = _rules.Count - 1; i >= 0; i--)
            if (_rules[i].Match((arg1, arg2, arg3)))
                return _rules[i].Factory((arg1, arg2, arg3));

        return _factory is null ? default! : _factory(arg1, arg2, arg3);
    }

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times, with any arguments.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertReceived(times, null, null);

    /// <summary>Asserts the number of calls whose arguments match. Omitted matchers match anything.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times, ArgMatcher<T1> arg1, ArgMatcher<T2> arg2 = default,
        ArgMatcher<T3> arg3 = default) =>
        AssertReceived(times,
            call => arg1.Matches(call.Arg1) && arg2.Matches(call.Arg2) && arg3.Matches(call.Arg3),
            $"arguments ({arg1}, {arg2}, {arg3})");

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertReceived(0, null, null);

    /// <summary>Asserts the method was never called with matching arguments.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was.</exception>
    public void DidNotReceive(ArgMatcher<T1> arg1, ArgMatcher<T2> arg2 = default,
        ArgMatcher<T3> arg3 = default) =>
        AssertReceived(0,
            call => arg1.Matches(call.Arg1) && arg2.Matches(call.Arg2) && arg3.Matches(call.Arg3),
            $"arguments ({arg1}, {arg2}, {arg3})");
}
