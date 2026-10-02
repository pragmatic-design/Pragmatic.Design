namespace Pragmatic.Testing.Mocking;

/// <summary>A mocked one-parameter method returning <typeparamref name="TResult"/>.</summary>
/// <typeparam name="T1">The parameter type.</typeparam>
/// <typeparam name="TResult">The return type.</typeparam>
public sealed class MockMethod<T1, TResult> : MockMethodBase<T1>
{
    private readonly List<(Func<T1, bool> Match, Func<T1, TResult> Factory)> _rules = [];
    private Func<T1, TResult>? _factory;

    /// <param name="name">The member's display name, e.g. <c>ICacheStack.RemoveAsync</c>.</param>
    public MockMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Configures the value returned for any argument.</summary>
    public MockMethod<T1, TResult> Returns(TResult value)
    {
        _factory = _ => value;
        return this;
    }

    /// <summary>Configures a factory over the argument, evaluated on every call.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockMethod<T1, TResult> Returns(Func<T1, TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockMethod<T1, TResult> Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>
    ///     Configures a result for calls whose argument matches, leaving other calls to whatever
    ///     <see cref="Returns(TResult)"/> configured:
    ///     <code>hasher.Hash.When("secret").Returns("hashed");</code>
    /// </summary>
    public MockMethodSetup<T1, TResult> When(ArgMatcher<T1> arg1) =>
        new(call => arg1.Matches(call), (match, factory) => _rules.Add((match, factory)));

    /// <summary>
    ///     Called by the generated explicit interface implementation. Records the argument, then
    ///     throws or returns the configured value — <c>default</c> when nothing was configured.
    /// </summary>
    public TResult Invoke(T1 arg1)
    {
        Record(arg1);

        // Most recent matching rule wins, so a later When() overrides an earlier one for the same
        // argument — the behaviour a reader expects from re-configuring a mock.
        for (var i = _rules.Count - 1; i >= 0; i--)
            if (_rules[i].Match(arg1))
                return _rules[i].Factory(arg1);

        return _factory is null ? default! : _factory(arg1);
    }

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times, with any argument.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertReceived(times, null, null);

    /// <summary>Asserts the number of calls whose argument matches.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times, ArgMatcher<T1> arg1) =>
        AssertReceived(times, call => arg1.Matches(call), $"argument {arg1}");

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertReceived(0, null, null);

    /// <summary>Asserts the method was never called with a matching argument.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was.</exception>
    public void DidNotReceive(ArgMatcher<T1> arg1) =>
        AssertReceived(0, call => arg1.Matches(call), $"argument {arg1}");
}
