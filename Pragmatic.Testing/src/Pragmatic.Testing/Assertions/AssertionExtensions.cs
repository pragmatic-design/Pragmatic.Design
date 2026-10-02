using System.Runtime.CompilerServices;

namespace Pragmatic.Testing.Assertions;

/// <summary>
///     The entry point: <c>value.Should()</c>, one overload per family.
/// </summary>
/// <remarks>
///     <para>
///         The fallback takes <see cref="object"/> rather than a generic <c>T</c>. With
///         <c>Should&lt;T&gt;(this T)</c> a <c>List&lt;int&gt;</c> would bind to it by identity
///         conversion and never reach the collection overload, which needs a conversion to
///         <c>IEnumerable&lt;int&gt;</c> — the more specific family would lose to the general one on
///         every call.
///     </para>
///     <para>
///         Every overload captures how the caller spelled the subject through
///         <see cref="CallerArgumentExpressionAttribute"/>, so a failure names <c>result.Value</c>
///         instead of "the value". The compiler knows it; recovering it from a stack trace, as the
///         library this replaces does, is guesswork by comparison.
///     </para>
/// </remarks>
public static class AssertionExtensions
{
    /// <summary>Assertions for any value without a more specific family.</summary>
    public static ObjectAssertions<object?> Should(
        this object? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a string.</summary>
    /// <remarks>
    ///     Ranked above the sequence overload: a string is an <c>IEnumerable&lt;char&gt;</c>, and
    ///     without this every <c>Contain("text")</c> would be asking for a single character.
    /// </remarks>
    [OverloadResolutionPriority(2)]
    public static StringAssertions Should(
        this string? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a boolean.</summary>
    public static BooleanAssertions Should(
        this bool subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a nullable boolean.</summary>
    public static BooleanAssertions Should(
        this bool? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>
    ///     Assertions for a value type with no more specific family — a domain struct such as
    ///     <c>LocalTime</c>.
    /// </summary>
    /// <remarks>
    ///     Without this, such a value falls back to the <see cref="object"/> overload, and
    ///     <c>result.Should().Be(default)</c> silently changes meaning: <c>default</c> is typed by
    ///     the parameter, so against <c>object?</c> it becomes <see langword="null"/> rather than the
    ///     struct's own default. Three Temporal tests caught exactly that.
    /// </remarks>
    public static ComparableAssertions<TSubject> Should<TSubject>(
        this TSubject subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
        where TSubject : struct =>
        new(subject, expression);

    /// <summary>Assertions for a sequence.</summary>
    /// <remarks>
    ///     Ranked above the value-type overload: <c>ImmutableArray&lt;T&gt;</c> is a struct as well
    ///     as a sequence, and it belongs here.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static CollectionAssertions<TItem> Should<TItem>(
        this IEnumerable<TItem>? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null) =>
        new(subject, expression);

    /// <summary>Assertions for a dictionary.</summary>
    /// <remarks>
    ///     Preferred over the <see cref="IDictionary{TKey,TValue}"/> overload, which a
    ///     <c>Dictionary&lt;K,V&gt;</c> matches just as well — without saying which wins, every call
    ///     on a concrete dictionary is ambiguous (CS0121).
    /// </remarks>
    [OverloadResolutionPriority(3)]
    public static DictionaryAssertions<TKey, TValue> Should<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue>? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
        where TKey : notnull =>
        new(subject, expression);

    /// <summary>Assertions for a dictionary.</summary>
    [OverloadResolutionPriority(2)]
    public static DictionaryAssertions<TKey, TValue> Should<TKey, TValue>(
        this IDictionary<TKey, TValue>? subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
        where TKey : notnull =>
        new(subject is null ? null : new Dictionary<TKey, TValue>(subject), expression);

    /// <summary>Assertions about what running an action does.</summary>
    public static ActionAssertions Should(
        this Action subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return new ActionAssertions(
            () =>
            {
                subject();
                return Task.CompletedTask;
            },
            expression);
    }

    /// <summary>Assertions about what awaiting an asynchronous action does.</summary>
    public static ActionAssertions Should(
        this Func<Task> subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new ActionAssertions(subject, expression);
    }

    /// <summary>Assertions about what awaiting an asynchronous function does.</summary>
    /// <remarks>
    ///     Separate from <see cref="Should(Func{Task},string)"/> because <c>Func&lt;Task&lt;T&gt;&gt;</c>
    ///     does not convert to it — a lambda returning a value would otherwise bind to the object
    ///     overload and assert on the delegate rather than on what it does.
    /// </remarks>
    public static FunctionAssertions<TResult> Should<TResult>(
        this Func<Task<TResult>> subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new FunctionAssertions<TResult>(subject, expression);
    }

    /// <summary>Assertions about what running a function does.</summary>
    public static FunctionAssertions<TResult> Should<TResult>(
        this Func<TResult> subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new FunctionAssertions<TResult>(() => Task.FromResult(subject()), expression);
    }

    /// <summary>Assertions about what awaiting a task does.</summary>
    public static ActionAssertions Should(
        this Func<ValueTask> subject,
        [CallerArgumentExpression(nameof(subject))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return new ActionAssertions(async () => await subject().ConfigureAwait(false), expression);
    }
}
