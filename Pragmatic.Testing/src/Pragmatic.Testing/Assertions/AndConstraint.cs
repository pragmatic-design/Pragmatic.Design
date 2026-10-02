namespace Pragmatic.Testing.Assertions;

/// <summary>
///     What an assertion returns, so another can be chained onto it:
///     <c>items.Should().HaveCount(3).And.Contain(x)</c>.
/// </summary>
/// <typeparam name="TAssertions">The assertion type <see cref="And"/> hands back.</typeparam>
public class AndConstraint<TAssertions>
{
    /// <summary>Public because generated assertions in other assemblies construct it.</summary>
    public AndConstraint(TAssertions parent) => And = parent;

    /// <summary>The assertion this one was called on, ready for the next check.</summary>
    public TAssertions And { get; }
}
