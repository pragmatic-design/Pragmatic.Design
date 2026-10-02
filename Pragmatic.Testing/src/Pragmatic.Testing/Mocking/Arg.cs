namespace Pragmatic.Testing.Mocking;

/// <summary>
///     Entry point for argument matchers. Keeps the call sites reading the way they did with the
///     substitute library this replaces — <c>Arg.Any&lt;string&gt;()</c>, <c>Arg.Is(42)</c> — while
///     producing an ordinary <see cref="ArgMatcher{T}"/> value rather than intercepting anything.
/// </summary>
public static class Arg
{
    /// <summary>Matches any argument of type <typeparamref name="T"/>, including <see langword="null"/>.</summary>
    /// <remarks>
    ///     Omitting the argument entirely has the same effect, because the default
    ///     <see cref="ArgMatcher{T}"/> is unconstrained. Write this where being explicit reads better.
    /// </remarks>
    public static ArgMatcher<T> Any<T>() => ArgMatcher<T>.Any;

    /// <summary>Matches arguments equal to <paramref name="value"/>.</summary>
    public static ArgMatcher<T> Is<T>(T value) => ArgMatcher<T>.Is(value);

    /// <summary>Matches arguments satisfying <paramref name="predicate"/>.</summary>
    public static ArgMatcher<T> Is<T>(Func<T, bool> predicate) => ArgMatcher<T>.Where(predicate);

    /// <summary>
    ///     Matches any argument and hands it to <paramref name="callback"/> — how a test captures the
    ///     object a member was called with, to assert on it afterwards.
    /// </summary>
    /// <remarks>See <see cref="ArgMatcher{T}.Capturing"/> for when the callback runs.</remarks>
    public static ArgMatcher<T> Do<T>(Action<T> callback) => ArgMatcher<T>.Capturing(callback);
}
