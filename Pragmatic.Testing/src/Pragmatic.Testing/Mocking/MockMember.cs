namespace Pragmatic.Testing.Mocking;

/// <summary>
///     Shared behaviour of every mocked member: the name it reports in failures, and the one place
///     that decides what a <c>Received</c> mismatch reads like.
/// </summary>
/// <remarks>
///     The name is supplied by the generator as <c>Type.Member</c>. It exists only for the failure
///     message, and that is not a detail: the reason to own this rather than depend on a substitute
///     library is lost if a failing expectation reports "expected 1, got 0" without saying of what.
/// </remarks>
public abstract class MockMember
{
    /// <param name="name">
    ///     The member's display name, normally <c>IClock.UtcNow</c>. Defaults to a placeholder so the
    ///     types stay usable when hand-written in a test.
    /// </param>
    protected MockMember(string? name = null) => Name = name ?? "<member>";

    /// <summary>The member's display name, as it appears in failure messages.</summary>
    public string Name { get; }

    /// <summary>
    ///     Asserts a call count, throwing a message that names the member, the expectation, the
    ///     actual count, and — when the check was constrained — what it was constrained to.
    /// </summary>
    /// <param name="expected">The expected number of matching calls.</param>
    /// <param name="matching">The number of calls that matched.</param>
    /// <param name="total">Total calls to the member, matching or not.</param>
    /// <param name="constraint">
    ///     A description of the argument constraint, or <see langword="null"/> when unconstrained.
    /// </param>
    /// <exception cref="PragmaticTestAssertionException">Thrown when the counts differ.</exception>
    protected void AssertCallCount(int expected, int matching, int total, string? constraint)
    {
        if (matching == expected)
            return;

        var with = constraint is null ? string.Empty : $" with {constraint}";
        var otherwise = constraint is null || total == matching
            ? string.Empty
            : $" ({total} call(s) to it in total, with other arguments)";

        throw new PragmaticTestAssertionException(
            $"Expected {Name} to be called {Times(expected)}{with}, but it was called {Times(matching)}{otherwise}.");
    }

    private static string Times(int n) => n == 1 ? "1 time" : $"{n} times";
}
