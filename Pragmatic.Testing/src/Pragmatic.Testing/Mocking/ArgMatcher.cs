namespace Pragmatic.Testing.Mocking;

/// <summary>
///     A predicate over one argument, used to configure a mocked member or to constrain a
///     <c>Received</c> check to the calls whose arguments match.
/// </summary>
/// <remarks>
///     <para>
///         Unlike the substitute libraries this replaces, a matcher here is an ordinary value: it is
///         passed as an argument and evaluated as a predicate. Nothing is intercepted, and no ambient
///         state records "the last call" — which is what makes the whole mechanism reflection-free
///         and safe under Native AOT.
///     </para>
///     <para>
///         A plain value converts implicitly, so <c>Received(1, "key")</c> and
///         <c>Received(1, Arg.Is&lt;string&gt;(k => k.StartsWith("k")))</c> are both valid.
///     </para>
/// </remarks>
/// <typeparam name="T">The argument type this matcher constrains.</typeparam>
public readonly struct ArgMatcher<T>
{
    private readonly Func<T, bool>? _predicate;
    private readonly string _description;

    private ArgMatcher(Func<T, bool>? predicate, string description)
    {
        _predicate = predicate;
        _description = description;
    }

    /// <summary>Matches any argument, including <see langword="null"/>.</summary>
    /// <remarks>
    ///     This is the default: a <c>default(ArgMatcher&lt;T&gt;)</c> — which is what an omitted
    ///     optional parameter produces — has a null predicate and matches everything. Omitting the
    ///     argument therefore means "any", with no separate overload needed.
    /// </remarks>
    public static ArgMatcher<T> Any => new(null, "any");

    /// <summary>Matches arguments equal to <paramref name="value"/> per <see cref="EqualityComparer{T}.Default"/>.</summary>
    public static ArgMatcher<T> Is(T value) =>
        new(actual => EqualityComparer<T>.Default.Equals(actual, value), Describe(value));

    /// <summary>Matches arguments satisfying <paramref name="predicate"/>.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
    public static ArgMatcher<T> Where(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new ArgMatcher<T>(predicate, "matching the given predicate");
    }

    /// <summary>
    ///     Matches any argument, handing it to <paramref name="callback"/> on the way — the way a test
    ///     captures the request object a member was called with.
    /// </summary>
    /// <remarks>
    ///     The callback runs whenever this matcher is evaluated, which includes the evaluation a
    ///     <c>Received</c> check performs. For a capture — the only thing this is for — that is
    ///     harmless: the same value is assigned twice. Do not put anything that counts in here.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="callback"/> is null.</exception>
    public static ArgMatcher<T> Capturing(Action<T> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        return new ArgMatcher<T>(
            actual =>
            {
                callback(actual);
                return true;
            },
            "any (captured)");
    }

    /// <summary>Whether <paramref name="actual"/> satisfies this matcher.</summary>
    public bool Matches(T actual) => _predicate is null || _predicate(actual);

    /// <summary>
    ///     How this matcher reads in a failure message — the expected value, or "any" when unconstrained.
    /// </summary>
    public override string ToString() => _description ?? "any";

    /// <summary>A plain value is the common case, and reads better than wrapping it in <see cref="Is"/>.</summary>
    public static implicit operator ArgMatcher<T>(T value) => Is(value);

    internal static string Describe(T value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        _ => value.ToString() ?? "<null>"
    };
}
