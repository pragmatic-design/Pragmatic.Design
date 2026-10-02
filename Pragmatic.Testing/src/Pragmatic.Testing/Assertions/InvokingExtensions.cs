namespace Pragmatic.Testing.Assertions;

/// <summary>
///     Turns "calling this member on that object" into a delegate the throw assertions can run:
///     <c>mock.Invoking(m =&gt; m.Get("k")).Should().Throw&lt;KeyNotFoundException&gt;()</c>.
/// </summary>
/// <remarks>
///     It reads better than declaring a local <c>Action</c> first, and it keeps the subject visible
///     in the assertion — which is the whole reason the callers here use it.
/// </remarks>
public static class InvokingExtensions
{
    /// <summary>Wraps a call on <paramref name="subject"/> as an action.</summary>
    public static Action Invoking<T>(this T subject, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return () => action(subject);
    }

    /// <summary>Wraps a call on <paramref name="subject"/> that returns a value.</summary>
    public static Func<TResult> Invoking<T, TResult>(this T subject, Func<T, TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return () => function(subject);
    }

    /// <summary>Wraps an asynchronous call on <paramref name="subject"/>.</summary>
    public static Func<Task> Awaiting<T>(this T subject, Func<T, Task> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return () => function(subject);
    }

    /// <summary>Wraps an asynchronous call on <paramref name="subject"/> that returns a value.</summary>
    public static Func<Task> Awaiting<T, TResult>(this T subject, Func<T, Task<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return async () => await function(subject).ConfigureAwait(false);
    }

    /// <summary>Wraps an asynchronous call returning a <see cref="ValueTask"/>.</summary>
    public static Func<Task> Awaiting<T>(this T subject, Func<T, ValueTask> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return async () => await function(subject).ConfigureAwait(false);
    }

    /// <summary>Wraps an asynchronous call returning a <see cref="ValueTask{TResult}"/>.</summary>
    public static Func<Task> Awaiting<T, TResult>(this T subject, Func<T, ValueTask<TResult>> function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return async () => await function(subject).ConfigureAwait(false);
    }
}
