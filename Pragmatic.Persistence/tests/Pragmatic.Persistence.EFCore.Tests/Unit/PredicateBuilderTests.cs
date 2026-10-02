using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Query;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     Tests for <see cref="PredicateBuilder"/> — expression combination with AND/OR logic.
/// </summary>
public class PredicateBuilderTests
{
    #region Or

    [Fact]
    public void Or_BothTrue_ReturnsTrue()
    {
        Expression<Func<int, bool>> left = x => x > 0;
        Expression<Func<int, bool>> right = x => x < 100;

        var combined = PredicateBuilder.Or(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeTrue();
    }

    [Fact]
    public void Or_LeftTrueRightFalse_ReturnsTrue()
    {
        Expression<Func<int, bool>> left = x => x > 0;
        Expression<Func<int, bool>> right = x => x > 100;

        var combined = PredicateBuilder.Or(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeTrue();
    }

    [Fact]
    public void Or_LeftFalseRightTrue_ReturnsTrue()
    {
        Expression<Func<int, bool>> left = x => x > 100;
        Expression<Func<int, bool>> right = x => x > 0;

        var combined = PredicateBuilder.Or(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeTrue();
    }

    [Fact]
    public void Or_BothFalse_ReturnsFalse()
    {
        Expression<Func<int, bool>> left = x => x > 100;
        Expression<Func<int, bool>> right = x => x < 0;

        var combined = PredicateBuilder.Or(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeFalse();
    }

    [Fact]
    public void Or_DifferentParameterNames_RebindsCorrectly()
    {
        Expression<Func<string, bool>> left = a => a.StartsWith("hello");
        Expression<Func<string, bool>> right = b => b.EndsWith("world");

        var combined = PredicateBuilder.Or(left, right);
        var fn = combined.Compile();

        fn("hello").Should().BeTrue();
        fn("world").Should().BeTrue();
        fn("goodbye").Should().BeFalse();
    }

    [Fact]
    public void Or_WorksWithLinq()
    {
        var data = new[] { 1, 2, 3, 4, 5, 6 }.AsQueryable();

        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> greaterThan4 = x => x > 4;

        var combined = PredicateBuilder.Or(isEven, greaterThan4);
        var result = data.Where(combined).ToList();

        result.Should().BeEquivalentTo([2, 4, 5, 6]);
    }

    #endregion

    #region And

    [Fact]
    public void And_BothTrue_ReturnsTrue()
    {
        Expression<Func<int, bool>> left = x => x > 0;
        Expression<Func<int, bool>> right = x => x < 100;

        var combined = PredicateBuilder.And(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeTrue();
    }

    [Fact]
    public void And_LeftTrueRightFalse_ReturnsFalse()
    {
        Expression<Func<int, bool>> left = x => x > 0;
        Expression<Func<int, bool>> right = x => x > 100;

        var combined = PredicateBuilder.And(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeFalse();
    }

    [Fact]
    public void And_BothFalse_ReturnsFalse()
    {
        Expression<Func<int, bool>> left = x => x > 100;
        Expression<Func<int, bool>> right = x => x < 0;

        var combined = PredicateBuilder.And(left, right);
        var fn = combined.Compile();

        fn(50).Should().BeFalse();
    }

    [Fact]
    public void And_DifferentParameterNames_RebindsCorrectly()
    {
        Expression<Func<string, bool>> left = a => a.Length > 3;
        Expression<Func<string, bool>> right = b => b.StartsWith("he");

        var combined = PredicateBuilder.And(left, right);
        var fn = combined.Compile();

        fn("hello").Should().BeTrue();
        fn("he").Should().BeFalse();    // length <= 3
        fn("world").Should().BeFalse(); // doesn't start with "he"
    }

    [Fact]
    public void And_WorksWithLinq()
    {
        var data = new[] { 1, 2, 3, 4, 5, 6 }.AsQueryable();

        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> greaterThan2 = x => x > 2;

        var combined = PredicateBuilder.And(isEven, greaterThan2);
        var result = data.Where(combined).ToList();

        result.Should().BeEquivalentTo([4, 6]);
    }

    #endregion

    #region Chaining

    [Fact]
    public void And_Then_Or_ChainsCorrectly()
    {
        Expression<Func<int, bool>> isPositive = x => x > 0;
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> isZero = x => x == 0;

        // (x > 0 AND x % 2 == 0) OR (x == 0)
        var andExpr = PredicateBuilder.And(isPositive, isEven);
        var orExpr = PredicateBuilder.Or(andExpr, isZero);
        var fn = orExpr.Compile();

        fn(0).Should().BeTrue();   // isZero
        fn(2).Should().BeTrue();   // positive and even
        fn(4).Should().BeTrue();   // positive and even
        fn(3).Should().BeFalse();  // positive but odd
        fn(-2).Should().BeFalse(); // even but negative
    }

    [Fact]
    public void Or_Then_And_ChainsCorrectly()
    {
        Expression<Func<int, bool>> lessThan3 = x => x < 3;
        Expression<Func<int, bool>> greaterThan7 = x => x > 7;
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;

        // (x < 3 OR x > 7) AND (x % 2 == 0)
        var orExpr = PredicateBuilder.Or(lessThan3, greaterThan7);
        var andExpr = PredicateBuilder.And(orExpr, isEven);
        var fn = andExpr.Compile();

        fn(2).Should().BeTrue();   // < 3 and even
        fn(8).Should().BeTrue();   // > 7 and even
        fn(1).Should().BeFalse();  // < 3 but odd
        fn(5).Should().BeFalse();  // neither < 3 nor > 7
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void And_WithComplexObjectExpression()
    {
        Expression<Func<TestItem, bool>> nameFilter = x => x.Name.Contains("Widget");
        Expression<Func<TestItem, bool>> priceFilter = x => x.Price > 10;

        var combined = PredicateBuilder.And(nameFilter, priceFilter);

        var data = new[]
        {
            new TestItem("Widget A", 15),
            new TestItem("Widget B", 5),
            new TestItem("Gadget C", 20),
        }.AsQueryable();

        var result = data.Where(combined).ToList();

        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Widget A");
    }

    [Fact]
    public void Or_WithComplexObjectExpression()
    {
        Expression<Func<TestItem, bool>> cheapFilter = x => x.Price < 5;
        Expression<Func<TestItem, bool>> nameFilter = x => x.Name == "Premium";

        var combined = PredicateBuilder.Or(cheapFilter, nameFilter);

        var data = new[]
        {
            new TestItem("Budget", 3),
            new TestItem("Premium", 100),
            new TestItem("Standard", 50),
        }.AsQueryable();

        var result = data.Where(combined).ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(x => x.Name == "Budget");
        result.Should().Contain(x => x.Name == "Premium");
    }

    #endregion

    private sealed record TestItem(string Name, decimal Price);
}
