namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A mocked generic method, configured per closed type argument:
///     <code>cache.GetAsync.Returns&lt;int&gt;(new ValueTask&lt;int?&gt;(42));</code>
/// </summary>
/// <remarks>
///     <para>
///         A generic method cannot have a typed member the way an ordinary one does — its result type
///         is not known until the caller closes it. So the results are keyed by the type argument,
///         which is the only part of the call that decides which configuration applies.
///     </para>
///     <para>
///         The trade-off is deliberate and worth knowing: <b>arguments are not recorded</b>, only the
///         type argument and the call count. <c>Received</c> here answers "how many times was this
///         closed over <c>T</c>", not "with which arguments". Exactly one call site in this repository
///         configures a generic method, so a fuller mechanism would be machinery in search of a use.
///     </para>
/// </remarks>
public sealed class MockGenericMethod : MockMember
{
    private readonly Dictionary<Type, object?> _byTypeArgument = [];
    private readonly Dictionary<Type, Func<object?[], object?>> _factoriesByTypeArgument = [];
    private readonly List<(Type TypeArgument, object?[] Arguments)> _calls = [];

    private readonly object? _default;

    /// <param name="name">The member's display name, e.g. <c>ICacheStack.GetAsync</c>.</param>
    /// <param name="defaultResult">
    ///     What an unconfigured call returns. Supplied by the generator, which knows the return type
    ///     at compile time — the same seeding the non-generic members get, so a Task-returning member
    ///     never hands back a null for the caller to await.
    /// </param>
    public MockGenericMethod(string? name = null, object? defaultResult = null) : base(name) =>
        _default = defaultResult;

    /// <summary>How many times the method was called, closed over any type.</summary>
    public int CallCount => _calls.Count;

    /// <summary>
    ///     Configures the result returned when the method is closed over <typeparamref name="TArg"/>.
    ///     The value is the method's <b>constructed return type</b> — for
    ///     <c>ValueTask&lt;T?&gt; GetAsync&lt;T&gt;(…)</c> closed over <c>int</c>, that is a
    ///     <c>ValueTask&lt;int?&gt;</c>.
    /// </summary>
    public MockGenericMethod Returns<TArg>(object? result)
    {
        _byTypeArgument[typeof(TArg)] = result;
        return this;
    }

    /// <summary>
    ///     Configures a result computed from the call's arguments, for the given type argument.
    /// </summary>
    /// <remarks>
    ///     The arguments arrive boxed, for the same reason the results are keyed by type: nothing
    ///     about the signature is known until the caller closes it. It is enough to stand in for a
    ///     real implementation — an in-memory cache honouring its own factory, say — which a fixed
    ///     value cannot do.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public MockGenericMethod Returns<TArg>(Func<object?[], object?> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factoriesByTypeArgument[typeof(TArg)] = factory;
        return this;
    }

    /// <summary>
    ///     Called by the generated explicit interface implementation. Records the call and returns
    ///     what was configured for <typeparamref name="TArg"/>, or <c>default</c>.
    /// </summary>
    /// <typeparam name="TArg">The type argument the caller closed the method over.</typeparam>
    /// <typeparam name="TReturn">The method's constructed return type.</typeparam>
    /// <param name="arguments">
    ///     The call's arguments, boxed. They cannot be typed here — the signature is only known once
    ///     the caller closes it — but recording them is what lets a test assert on WHICH call
    ///     happened, not merely how many.
    /// </param>
    public TReturn Invoke<TArg, TReturn>(params object?[] arguments)
    {
        _calls.Add((typeof(TArg), arguments));

        if (_factoriesByTypeArgument.TryGetValue(typeof(TArg), out var factory)
            && factory(arguments) is TReturn computed)
            return computed;

        if (_byTypeArgument.TryGetValue(typeof(TArg), out var configured) && configured is TReturn typed)
            return typed;

        return _default is TReturn seeded ? seeded : default!;
    }

    /// <summary>
    ///     The same for a generic method returning <see langword="void"/>, which cannot go through
    ///     <see cref="Invoke{TArg,TReturn}"/> — <c>void</c> is not a valid type argument.
    /// </summary>
    /// <typeparam name="TArg">The type argument the caller closed the method over.</typeparam>
    public void InvokeVoid<TArg>(params object?[] arguments) => _calls.Add((typeof(TArg), arguments));

    /// <summary>Asserts the method was called exactly <paramref name="times"/> times, over any type.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times = 1) => AssertCallCount(times, _calls.Count, _calls.Count, null);

    /// <summary>
    ///     Asserts the number of calls closed over <typeparamref name="TArg"/>, optionally constrained
    ///     by a predicate over the boxed arguments.
    /// </summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received<TArg>(int times = 1, Func<object?[], bool>? where = null)
    {
        var matching = 0;
        foreach (var call in _calls)
            if (call.TypeArgument == typeof(TArg) && (where is null || where(call.Arguments)))
                matching++;

        AssertCallCount(times, matching, _calls.Count, $"type argument {typeof(TArg).Name}");
    }

    /// <summary>
    ///     Asserts the number of calls whose arguments satisfy <paramref name="where"/>, over any type.
    /// </summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the count differs.</exception>
    public void Received(int times, Func<object?[], bool> where)
    {
        ArgumentNullException.ThrowIfNull(where);

        var matching = 0;
        foreach (var call in _calls)
            if (where(call.Arguments))
                matching++;

        AssertCallCount(times, matching, _calls.Count, "the given argument predicate");
    }

    /// <summary>Asserts the method was never called.</summary>
    /// <exception cref="PragmaticTestAssertionException">Thrown when it was called.</exception>
    public void DidNotReceive() => AssertCallCount(0, _calls.Count, _calls.Count, null);

    /// <summary>Forgets every recorded call, leaving the configuration in place.</summary>
    public void ClearReceivedCalls() => _calls.Clear();
}
