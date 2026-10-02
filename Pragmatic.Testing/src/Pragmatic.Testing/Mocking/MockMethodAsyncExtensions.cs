namespace Pragmatic.Testing.Mocking;

/// <summary>
///     Lets an async member be configured with the value it resolves to, rather than with a task
///     wrapping it: <c>store.GetAsync.Returns("v")</c> instead of
///     <c>store.GetAsync.Returns(Task.FromResult("v"))</c>.
/// </summary>
/// <remarks>
///     <para>
///         The substitute library this replaces wrapped the value automatically, and the test suites
///         are written expecting that. These extensions restore it without adding a parallel family
///         of async mock types: they apply only where the return type is a task, so the compiler
///         picks them exactly when the argument is the inner value and the instance method when it
///         is already a task.
///     </para>
///     <para>
///         Only the arities the repository actually uses are here. Adding one is three lines.
///     </para>
/// </remarks>
public static class MockMethodAsyncExtensions
{
    /// <summary>Configures a parameterless <c>Task&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<Task<TResult>> Returns<TResult>(
        this MockMethod<Task<TResult>> method, TResult value) =>
        method.Returns(Task.FromResult(value));

    /// <summary>Configures a one-parameter <c>Task&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<T1, Task<TResult>> Returns<T1, TResult>(
        this MockMethod<T1, Task<TResult>> method, TResult value) =>
        method.Returns(Task.FromResult(value));

    /// <summary>Configures a two-parameter <c>Task&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<T1, T2, Task<TResult>> Returns<T1, T2, TResult>(
        this MockMethod<T1, T2, Task<TResult>> method, TResult value) =>
        method.Returns(Task.FromResult(value));

    /// <summary>Configures a three-parameter <c>Task&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<T1, T2, T3, Task<TResult>> Returns<T1, T2, T3, TResult>(
        this MockMethod<T1, T2, T3, Task<TResult>> method, TResult value) =>
        method.Returns(Task.FromResult(value));

    /// <summary>Configures a parameterless <c>ValueTask&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<ValueTask<TResult>> Returns<TResult>(
        this MockMethod<ValueTask<TResult>> method, TResult value) =>
        method.Returns(new ValueTask<TResult>(value));

    /// <summary>Configures a one-parameter <c>ValueTask&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<T1, ValueTask<TResult>> Returns<T1, TResult>(
        this MockMethod<T1, ValueTask<TResult>> method, TResult value) =>
        method.Returns(new ValueTask<TResult>(value));

    /// <summary>Configures a two-parameter <c>ValueTask&lt;T&gt;</c> member with its resolved value.</summary>
    public static MockMethod<T1, T2, ValueTask<TResult>> Returns<T1, T2, TResult>(
        this MockMethod<T1, T2, ValueTask<TResult>> method, TResult value) =>
        method.Returns(new ValueTask<TResult>(value));

    /// <summary>
    ///     The same for a conditional setup: <c>When(...).Returns(value)</c> has to accept the
    ///     resolved value too, or the two ways of configuring an async member disagree.
    /// </summary>
    public static void Returns<TArgs, TResult>(
        this MockMethodSetup<TArgs, Task<TResult>> setup, TResult value) =>
        setup.Returns(Task.FromResult(value));

    /// <summary>Conditional setup of a <c>ValueTask&lt;T&gt;</c> member, with its resolved value.</summary>
    public static void Returns<TArgs, TResult>(
        this MockMethodSetup<TArgs, ValueTask<TResult>> setup, TResult value) =>
        setup.Returns(new ValueTask<TResult>(value));
}
