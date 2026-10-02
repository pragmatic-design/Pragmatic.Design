namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A mocked parameterless method returning <see langword="void"/>.
/// </summary>
/// <remarks>
///     Genuinely void members are rare on the interfaces being mocked — an async method returns
///     <c>Task</c> or <c>ValueTask</c>, which is an ordinary return type handled by
///     <see cref="MockMethod{TResult}"/>. Only two void arities exist for that reason; add another
///     when a member needs it.
/// </remarks>
public sealed class MockVoidMethod : MockMethodBase<ValueTuple>
{
    private Action? _callback;

    /// <param name="name">The member's display name.</param>
    public MockVoidMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Runs <paramref name="callback"/> on every call, for a side effect the test observes.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    public MockVoidMethod Does(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockVoidMethod Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>Called by the generated explicit interface implementation.</summary>
    public void Invoke()
    {
        Record(default);
        _callback?.Invoke();
    }

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertReceived(times, null, null);

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertReceived(0, null, null);
}
