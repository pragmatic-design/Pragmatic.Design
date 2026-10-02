namespace Pragmatic.Testing.Mocking;

/// <summary>A mocked three-parameter method returning <see langword="void"/>.</summary>
/// <typeparam name="T1">The first parameter type.</typeparam>
/// <typeparam name="T2">The second parameter type.</typeparam>
/// <typeparam name="T3">The third parameter type.</typeparam>
public sealed class MockVoidMethod<T1, T2, T3> : MockMethodBase<(T1 Arg1, T2 Arg2, T3 Arg3)>
{
    private Action<T1, T2, T3>? _callback;

    /// <param name="name">The member's display name.</param>
    public MockVoidMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Runs <paramref name="callback"/> on every call, for a side effect the test observes.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    public MockVoidMethod<T1, T2, T3> Does(Action<T1, T2, T3> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockVoidMethod<T1, T2, T3> Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>Called by the generated explicit interface implementation.</summary>
    public void Invoke(T1 arg1, T2 arg2, T3 arg3)
    {
        Record((arg1, arg2, arg3));
        _callback?.Invoke(arg1, arg2, arg3);
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
