namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions about a delegate that returns a value.
/// </summary>
/// <remarks>
///     Separate from <see cref="ActionAssertions"/> for one reason: <c>NotThrow</c> hands back what
///     the delegate returned, so a test can assert that constructing something succeeds and then go
///     on to assert about the thing — <c>var logger = act.Should().NotThrow().Subject;</c>.
/// </remarks>
/// <typeparam name="TResult">What the delegate returns.</typeparam>
public sealed class FunctionAssertions<TResult>
{
    private readonly Func<Task<TResult>> _function;
    private readonly string? _expression;

    internal FunctionAssertions(Func<Task<TResult>> function, string? expression)
    {
        _function = function;
        _expression = expression;
    }

    /// <summary>Fails unless running the delegate throws <typeparamref name="TException"/> or a subclass.</summary>
    public ExceptionAssertions<TException> Throw<TException>(string? because = null, params object[] becauseArgs)
        where TException : Exception =>
        ThrowAsync<TException>(because, becauseArgs).GetAwaiter().GetResult();

    /// <summary>Fails unless running the delegate throws exactly <typeparamref name="TException"/>.</summary>
    public ExceptionAssertions<TException> ThrowExactly<TException>(
        string? because = null, params object[] becauseArgs)
        where TException : Exception
    {
        var assertions = Throw<TException>(because, becauseArgs);

        if (assertions.Subject.GetType() != typeof(TException))
            AssertionFailure.Throw(_expression, $"to throw exactly {typeof(TException).Name}",
                $"found {assertions.Subject.GetType().Name}", because, becauseArgs);

        return assertions;
    }

    /// <summary>Fails unless running the delegate throws <typeparamref name="TException"/> or a subclass.</summary>
    public async Task<ExceptionAssertions<TException>> ThrowAsync<TException>(
        string? because = null, params object[] becauseArgs)
        where TException : Exception
    {
        var (_, caught) = await CaptureAsync().ConfigureAwait(false);

        if (caught is not TException expected)
        {
            AssertionFailure.Throw(_expression, $"to throw {typeof(TException).Name}",
                caught is null ? "it did not throw" : $"it threw {caught.GetType().Name}: {caught.Message}",
                because, becauseArgs);
            expected = null!;
        }

        return new ExceptionAssertions<TException>(expected, _expression);
    }

    /// <summary>Fails when running the delegate throws; hands back what it returned.</summary>
    public AndWhichConstraint<FunctionAssertions<TResult>, TResult> NotThrow(
        string? because = null, params object[] becauseArgs) =>
        NotThrowAsync(because, becauseArgs).GetAwaiter().GetResult();

    /// <summary>Fails when running the delegate throws; hands back what it returned.</summary>
    public async Task<AndWhichConstraint<FunctionAssertions<TResult>, TResult>> NotThrowAsync(
        string? because = null, params object[] becauseArgs)
    {
        var (result, caught) = await CaptureAsync().ConfigureAwait(false);

        if (caught is not null)
            AssertionFailure.Throw(_expression, "not to throw",
                $"it threw {caught.GetType().Name}: {caught.Message}", because, becauseArgs);

        return new AndWhichConstraint<FunctionAssertions<TResult>, TResult>(this, result!);
    }

    /// <summary>Fails when running the delegate throws <typeparamref name="TException"/>.</summary>
    public void NotThrow<TException>(string? because = null, params object[] becauseArgs)
        where TException : Exception
    {
        var (_, caught) = CaptureAsync().GetAwaiter().GetResult();

        if (caught is TException)
            AssertionFailure.Throw(_expression, $"not to throw {typeof(TException).Name}",
                "it did", because, becauseArgs);
    }

    /// <summary>Runs the delegate, keeping both what it returned and what it threw.</summary>
    private async Task<(TResult? Result, Exception? Caught)> CaptureAsync()
    {
        try
        {
            return (await _function().ConfigureAwait(false), null);
        }
        catch (Exception exception)
        {
            return (default, exception);
        }
    }
}
