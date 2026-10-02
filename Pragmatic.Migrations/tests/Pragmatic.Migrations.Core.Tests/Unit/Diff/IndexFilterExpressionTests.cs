using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Diff;

namespace Pragmatic.Migrations.Core.Tests.Unit.Diff;

/// <summary>
///     A partial index's predicate compares as a predicate: each database's spelling of the same one is equal,
///     and a different one is not.
/// </summary>
public class IndexFilterExpressionTests
{
    [Theory]
    [InlineData("\"Status\" = 0")] // as a model declares it
    [InlineData("(\"Status\" = 0)")] // as PostgreSQL returns it
    [InlineData("([Status]=(0))")] // as SQL Server returns it
    public void TheSamePredicate_SpelledByEachDatabase_IsEqual(string spelling)
        => IndexFilterExpression.Normalize(spelling).Should().Be(IndexFilterExpression.Normalize("\"Status\" = 0"));

    [Fact]
    public void AStringLiteral_KeepsItsSpacesAndCase()
        => IndexFilterExpression.Normalize("\"Name\" = 'A b'").Should().NotBe(IndexFilterExpression.Normalize("\"Name\" = 'ab'"));

    /// <summary>The control: grouping decides the predicate, so different grouping stays different.</summary>
    [Fact]
    public void DifferentGrouping_IsNotEqual()
        => IndexFilterExpression.Normalize("a = 1 AND (b = 1 OR c = 1)")
            .Should().NotBe(IndexFilterExpression.Normalize("(a = 1 AND b = 1) OR c = 1"));

    /// <summary>The control: another value is another predicate.</summary>
    [Fact]
    public void ADifferentValue_IsNotEqual()
        => IndexFilterExpression.Normalize("([Status]=(1))").Should().NotBe(IndexFilterExpression.Normalize("\"Status\" = 0"));

    [Fact]
    public void NoFilter_IsNull()
        => IndexFilterExpression.Normalize("  ").Should().BeNull();
}
