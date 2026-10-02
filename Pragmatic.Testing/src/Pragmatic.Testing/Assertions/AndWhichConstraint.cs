namespace Pragmatic.Testing.Assertions;

/// <summary>
///     An <see cref="AndConstraint{TAssertions}"/> that also carries the value the assertion picked
///     out, so the next check can be made against it:
///     <c>items.Should().ContainSingle().Which.Name.Should().Be("x")</c>.
/// </summary>
/// <typeparam name="TAssertions">The assertion type <c>And</c> hands back.</typeparam>
/// <typeparam name="TMatched">The type of the value that was picked out.</typeparam>
public sealed class AndWhichConstraint<TAssertions, TMatched> : AndConstraint<TAssertions>
{
    /// <summary>Public because generated assertions in other assemblies construct it.</summary>
    public AndWhichConstraint(TAssertions parent, TMatched matched) : base(parent) => Which = matched;

    /// <summary>The value the assertion selected — the single element, the found item, the exception.</summary>
    public TMatched Which { get; }

    /// <summary>The same value as <see cref="Which"/>. Both spellings are in use.</summary>
    public TMatched Subject => Which;

    /// <summary>The same value again, as a dictionary lookup reads: <c>ContainKey(k).WhoseValue</c>.</summary>
    public TMatched WhoseValue => Which;
}
