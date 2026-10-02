namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A pending conditional configuration: the matchers have been given, the result has not.
///     Produced by <c>When(...)</c> and completed by <c>Returns</c> or <c>Throws</c>.
/// </summary>
/// <remarks>
///     This exists because a mocked method often has to answer differently depending on its
///     arguments — <c>Verify("correct", hash)</c> true, <c>Verify("wrong", hash)</c> false — and
///     collapsing both into one unconditional result is the kind of change that leaves a test green
///     while it stops checking anything.
/// </remarks>
/// <typeparam name="TArgs">The recorded argument shape of the method being configured.</typeparam>
/// <typeparam name="TResult">The method's return type.</typeparam>
public sealed class MockMethodSetup<TArgs, TResult>
{
    private readonly Func<TArgs, bool> _predicate;
    private readonly Action<Func<TArgs, bool>, Func<TArgs, TResult>> _register;

    internal MockMethodSetup(
        Func<TArgs, bool> predicate,
        Action<Func<TArgs, bool>, Func<TArgs, TResult>> register)
    {
        _predicate = predicate;
        _register = register;
    }

    /// <summary>Returns <paramref name="value"/> for calls matching this setup.</summary>
    public void Returns(TResult value) => _register(_predicate, _ => value);

    /// <summary>Returns a value computed from the arguments, for calls matching this setup.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public void Returns(Func<TArgs, TResult> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _register(_predicate, factory);
    }

    /// <summary>Throws <paramref name="exception"/> for calls matching this setup.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
    public void Throws(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _register(_predicate, _ => throw exception);
    }
}
