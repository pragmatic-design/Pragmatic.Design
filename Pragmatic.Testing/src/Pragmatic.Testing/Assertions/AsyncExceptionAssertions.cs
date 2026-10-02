namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Lets the exception assertions be chained onto <c>ThrowAsync</c> before it is awaited:
///     <c>await act.Should().ThrowAsync&lt;IOException&gt;().WithMessage("*locked*")</c>.
/// </summary>
/// <remarks>
///     Without these, the caller would have to await the throw first and assert on the result in a
///     second statement — which is not how any of the ~220 async throw assertions here are written.
/// </remarks>
public static class AsyncExceptionAssertions
{
    /// <summary>Awaits the throw assertion, then checks the message.</summary>
    public static async Task<ExceptionAssertions<TException>> WithMessage<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        string wildcardPattern,
        string? because = null,
        params object[] becauseArgs)
        where TException : Exception =>
        (await assertions.ConfigureAwait(false)).WithMessage(wildcardPattern, because, becauseArgs);

    /// <summary>Awaits the throw assertion, then checks the parameter name.</summary>
    public static async Task<ExceptionAssertions<TException>> WithParameterName<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        string expected,
        string? because = null,
        params object[] becauseArgs)
        where TException : Exception =>
        (await assertions.ConfigureAwait(false)).WithParameterName(expected, because, becauseArgs);

    /// <summary>Awaits the throw assertion, then checks the inner exception.</summary>
    public static async Task<AndWhichConstraint<ExceptionAssertions<TException>, TInner>>
        WithInnerException<TException, TInner>(
            this Task<ExceptionAssertions<TException>> assertions,
            string? because = null,
            params object[] becauseArgs)
        where TException : Exception
        where TInner : Exception =>
        (await assertions.ConfigureAwait(false)).WithInnerException<TInner>(because, becauseArgs);

    /// <summary>Awaits the throw assertion, then applies a predicate to the exception.</summary>
    public static async Task<ExceptionAssertions<TException>> Where<TException>(
        this Task<ExceptionAssertions<TException>> assertions,
        Func<TException, bool> predicate,
        string? because = null,
        params object[] becauseArgs)
        where TException : Exception =>
        (await assertions.ConfigureAwait(false)).Where(predicate, because, becauseArgs);

    /// <summary>Awaits the throw assertion and hands back the exception itself.</summary>
    /// <remarks>
    ///     For <c>(await act.Should().ThrowAsync&lt;T&gt;()).Which</c> written the other way round:
    ///     <c>await act.Should().ThrowAsync&lt;T&gt;().Subject()</c> reads no better, so this exists
    ///     mainly so a caller holding the task can still reach the exception.
    /// </remarks>
    public static async Task<TException> Subject<TException>(
        this Task<ExceptionAssertions<TException>> assertions)
        where TException : Exception =>
        (await assertions.ConfigureAwait(false)).Subject;
}
