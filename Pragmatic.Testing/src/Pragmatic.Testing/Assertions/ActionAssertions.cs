namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Assertions about what running a delegate does: whether it throws, and what.
/// </summary>
/// <remarks>
///     The subject is the delegate, not its result — <c>var act = () => storage.Save(…);</c>
///     followed by <c>act.Should().Throw&lt;IOException&gt;()</c>. Running it is the assertion.
/// </remarks>
public sealed class ActionAssertions
{
    private readonly Func<Task> _action;
    private readonly string? _expression;

    internal ActionAssertions(Func<Task> action, string? expression)
    {
        _action = action;
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
        var caught = await CaptureAsync().ConfigureAwait(false);

        if (caught is not TException expected)
        {
            AssertionFailure.Throw(_expression, $"to throw {typeof(TException).Name}",
                caught is null ? "it did not throw" : $"it threw {caught.GetType().Name}: {caught.Message}",
                because, becauseArgs);
            expected = null!;
        }

        return new ExceptionAssertions<TException>(expected, _expression);
    }

    /// <summary>Fails when running the delegate throws anything.</summary>
    public void NotThrow(string? because = null, params object[] becauseArgs) =>
        NotThrowAsync(because, becauseArgs).GetAwaiter().GetResult();

    /// <summary>Fails when running the delegate throws anything.</summary>
    public async Task NotThrowAsync(string? because = null, params object[] becauseArgs)
    {
        var caught = await CaptureAsync().ConfigureAwait(false);

        if (caught is not null)
            AssertionFailure.Throw(_expression, "not to throw",
                $"it threw {caught.GetType().Name}: {caught.Message}", because, becauseArgs);
    }

    /// <summary>Fails when running the delegate throws <typeparamref name="TException"/>.</summary>
    public void NotThrow<TException>(string? because = null, params object[] becauseArgs)
        where TException : Exception
    {
        var caught = CaptureAsync().GetAwaiter().GetResult();

        if (caught is TException)
            AssertionFailure.Throw(_expression, $"not to throw {typeof(TException).Name}",
                "it did", because, becauseArgs);
    }

    /// <summary>Runs the delegate and hands back what it threw, or null.</summary>
    private async Task<Exception?> CaptureAsync()
    {
        try
        {
            await _action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
