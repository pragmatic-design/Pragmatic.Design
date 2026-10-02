namespace Pragmatic.Testing.Mocking;

/// <summary>A mocked parameterless method returning <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The return type.</typeparam>
public sealed class MockMethod<TResult> : MockMethodBase<ValueTuple>
{
    private Func<TResult>? _factory;

    /// <param name="name">The member's display name, e.g. <c>IClock.GetTimeProvider</c>.</param>
    public MockMethod(string? name = null) : base(name)
    {
    }

    /// <summary>Configures the value returned by every call.</summary>
    public MockMethod<TResult> Returns(TResult value)
    {
        _factory = () => value;
        return this;
    }

    /// <summary>Configures a factory evaluated on every call.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockMethod<TResult> Returns(Func<TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        return this;
    }

    /// <summary>Configures the method to throw when called.</summary>
    public MockMethod<TResult> Throws(Exception exception)
    {
        SetThrows(exception);
        return this;
    }

    /// <summary>
    ///     Called by the generated explicit interface implementation. Records the call, then throws
    ///     or returns the configured value — <c>default</c> when nothing was configured.
    /// </summary>
    public TResult Invoke()
    {
        Record(default);
        return _factory is null ? default! : _factory();
    }

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertReceived(times, null, null);

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertReceived(0, null, null);
}
