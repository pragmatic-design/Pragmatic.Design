namespace Pragmatic.Testing.Mocking;

/// <summary>A mocked one-parameter method returning <see langword="void"/>.</summary>
/// <typeparam name="T1">The parameter type.</typeparam>
public sealed class MockVoidMethod<T1> : MockMethodBase<T1>
{
    private Action<T1>? _callback;

    /// <param name="name">The member's display name, e.g. <c>IUnitOfWork.Add</c>.</param>
    public MockVoidMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Runs <paramref name="callback"/> on every call, for a side effect the test observes.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    public MockVoidMethod<T1> Does(Action<T1> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockVoidMethod<T1> Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>Called by the generated explicit interface implementation.</summary>
    public void Invoke(T1 arg1)
    {
        Record(arg1);
        _callback?.Invoke(arg1);
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
