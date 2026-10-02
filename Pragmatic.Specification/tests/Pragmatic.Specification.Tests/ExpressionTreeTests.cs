using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

/// <summary>
///     Tests to ensure expression trees are correctly built and can be used with LINQ to SQL-like providers.
///     The key requirement is that composed expressions don't use Expression.Invoke (which fails in EF Core).
/// </summary>
public class ExpressionTreeTests
{
    [Fact]
    public void AndSpecification_ProducesValidExpressionTree()
    {
        // Arrange
        var isActive = Spec<User>.Where(u => u.IsActive);
        var isAdmin = Spec<User>.Where(u => u.Role == "Admin");
        var combined = isActive.And(isAdmin);

        // Act
        var expression = combined.ToExpression();

        // Assert - verify it's a single parameter lambda with AndAlso body
        expression.Parameters.Should().HaveCount(1);
        expression.Body.NodeType.Should().Be(ExpressionType.AndAlso);
    }

    [Fact]
    public void OrSpecification_ProducesValidExpressionTree()
    {
        // Arrange
        var isActive = Spec<User>.Where(u => u.IsActive);
        var isAdmin = Spec<User>.Where(u => u.Role == "Admin");
        var combined = isActive.Or(isAdmin);

        // Act
        var expression = combined.ToExpression();

        // Assert
        expression.Parameters.Should().HaveCount(1);
        expression.Body.NodeType.Should().Be(ExpressionType.OrElse);
    }

    [Fact]
    public void NotSpecification_ProducesValidExpressionTree()
    {
        // Arrange
        var isActive = Spec<User>.Where(u => u.IsActive);
        var notActive = isActive.Not();

        // Act
        var expression = notActive.ToExpression();

        // Assert
        expression.Parameters.Should().HaveCount(1);
        expression.Body.NodeType.Should().Be(ExpressionType.Not);
    }

    [Fact]
    public void ComplexComposition_ProducesValidExpressionTree()
    {
        // Arrange: (IsActive AND IsAdmin) OR (Age > 18 AND NOT IsDeleted)
        var isActive = Spec<User>.Where(u => u.IsActive);
        var isAdmin = Spec<User>.Where(u => u.Role == "Admin");
        var isAdult = Spec<User>.Where(u => u.Age > 18);
        var isNotDeleted = Spec<User>.Where(u => !u.IsDeleted);

        var complex = isActive.And(isAdmin).Or(isAdult.And(isNotDeleted));

        // Act
        var expression = complex.ToExpression();

        // Assert - the root should be OrElse
        expression.Parameters.Should().HaveCount(1);
        expression.Body.NodeType.Should().Be(ExpressionType.OrElse);

        // Verify it works with queryable (simulates EF Core usage)
        var users = new List<User>
        {
            new() { IsActive = true, Role = "Admin", Age = 15, IsDeleted = false },
            new() { IsActive = false, Role = "User", Age = 25, IsDeleted = false },
            new() { IsActive = false, Role = "User", Age = 25, IsDeleted = true },
            new() { IsActive = false, Role = "User", Age = 15, IsDeleted = false }
        }.AsQueryable();

        var result = users.Where(expression).ToList();
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Expression_CanBeUsedDirectlyInLinq()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive && u.Age >= 18);
        var users = new List<User>
        {
            new() { IsActive = true, Age = 25 },
            new() { IsActive = true, Age = 15 },
            new() { IsActive = false, Age = 30 }
        }.AsQueryable();

        // Act - use expression directly
        var result = users.Where(spec.ToExpression()).ToList();

        // Assert
        result.Should().HaveCount(1);
        result[0].Age.Should().Be(25);
    }

    [Fact]
    public void DifferentParameterNames_AreUnified()
    {
        // This test ensures parameter replacement works even with different lambda parameter names
        // Arrange
        var spec1 = Spec<User>.Where(u => u.IsActive);
        var spec2 = Spec<User>.Where(x => x.Role == "Admin");
        var spec3 = Spec<User>.Where(user => user.Age >= 18);

        var combined = spec1.And(spec2).And(spec3);

        // Act
        var expression = combined.ToExpression();

        // Assert - should have single unified parameter
        expression.Parameters.Should().HaveCount(1);

        // And it should work correctly
        var users = new List<User>
        {
            new() { IsActive = true, Role = "Admin", Age = 25 },
            new() { IsActive = true, Role = "Admin", Age = 15 },
            new() { IsActive = false, Role = "Admin", Age = 25 }
        };

        var result = users.Where(expression.Compile()).ToList();
        result.Should().HaveCount(1);
    }
}