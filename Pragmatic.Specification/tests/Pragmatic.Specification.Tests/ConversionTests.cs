using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class ConversionTests
{
    [Fact]
    public void ToExpression_ReturnsValidExpression()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive);

        // Act
        var expr = spec.ToExpression();

        // Assert
        expr.Should().NotBeNull();
        var compiled = expr.Compile();
        compiled(new User { IsActive = true }).Should().BeTrue();
        compiled(new User { IsActive = false }).Should().BeFalse();
    }

    [Fact]
    public void IsSatisfiedBy_WorksAsDelegate()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive);

        // Act - use IsSatisfiedBy as delegate
        Func<User, bool> func = spec.IsSatisfiedBy;

        // Assert
        func.Should().NotBeNull();
        func(new User { IsActive = true }).Should().BeTrue();
        func(new User { IsActive = false }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_WithComplexSpec_ReturnsValidExpression()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.Age > 18 && u.IsActive);

        // Act
        var expr = spec.ToExpression();

        // Assert
        expr.Should().NotBeNull();
        expr.Parameters.Should().HaveCount(1);
        expr.Body.Should().NotBeNull();
    }

    [Fact]
    public void ComposedSpec_ToExpression_ReturnsValidExpression()
    {
        // Arrange
        var isActive = Spec<User>.Where(u => u.IsActive);
        var isAdmin = Spec<User>.Where(u => u.Role == "Admin");
        var spec = isActive.And(isAdmin);

        // Act
        var expr = spec.ToExpression();

        // Assert
        expr.Should().NotBeNull();
        expr.Parameters.Should().HaveCount(1);

        // The expression should work correctly
        var compiled = expr.Compile();
        compiled(new User { IsActive = true, Role = "Admin" }).Should().BeTrue();
        compiled(new User { IsActive = true, Role = "User" }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_CanBeUsedDirectlyInLinqQueries()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive);
        var users = new List<User>
        {
            new() { IsActive = true },
            new() { IsActive = false }
        }.AsQueryable();

        // Act - use expression directly in LINQ
        var result = users.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].IsActive.Should().BeTrue();
    }
}