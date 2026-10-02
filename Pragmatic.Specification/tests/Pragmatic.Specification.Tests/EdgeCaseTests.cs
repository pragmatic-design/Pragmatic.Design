using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class EdgeCaseTests
{
    private static Specification<User> IsActive => Spec<User>.Where(u => u.IsActive);
    private static Specification<User> IsAdmin => Spec<User>.Where(u => u.Role == "Admin");
    private static Specification<User> IsAdult => Spec<User>.Where(u => u.Age >= 18);

    [Fact]
    public void And_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var spec = IsActive;

        // Act
        var act = () => spec.And(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Or_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var spec = IsActive;

        // Act
        var act = () => spec.Or(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void True_And_False_ReturnsFalse()
    {
        // True AND False = False (identity behavior)
        var spec = Spec<User>.True.And(Spec<User>.False);

        spec.IsSatisfiedBy(new User { IsActive = true }).Should().BeFalse();
        spec.IsSatisfiedBy(new User { IsActive = false }).Should().BeFalse();
    }

    [Fact]
    public void True_Or_False_ReturnsTrue()
    {
        // True OR False = True (identity behavior)
        var spec = Spec<User>.True.Or(Spec<User>.False);

        spec.IsSatisfiedBy(new User { IsActive = true }).Should().BeTrue();
        spec.IsSatisfiedBy(new User { IsActive = false }).Should().BeTrue();
    }

    [Fact]
    public void False_And_True_ReturnsFalse()
    {
        var spec = Spec<User>.False.And(Spec<User>.True);

        spec.IsSatisfiedBy(new User()).Should().BeFalse();
    }

    [Fact]
    public void False_Or_True_ReturnsTrue()
    {
        var spec = Spec<User>.False.Or(Spec<User>.True);

        spec.IsSatisfiedBy(new User()).Should().BeTrue();
    }

    [Fact]
    public void DeepComposition_FiveLevels_WorksCorrectly()
    {
        // Build a 5-level deep composition: ((Active AND Admin) OR Adult) AND (NOT Deleted) AND HasEmail
        var isNotDeleted = Spec<User>.Where(u => !u.IsDeleted);
        var hasEmail = Spec<User>.Where(u => !string.IsNullOrEmpty(u.Email));

        var spec = IsActive
            .And(IsAdmin)
            .Or(IsAdult)
            .And(isNotDeleted)
            .And(hasEmail);

        // Active admin with email, not deleted
        var match1 = new User { IsActive = true, Role = "Admin", Age = 15, IsDeleted = false, Email = "a@b.com" };
        spec.IsSatisfiedBy(match1).Should().BeTrue();

        // Adult non-admin with email, not deleted
        var match2 = new User { IsActive = false, Role = "User", Age = 25, IsDeleted = false, Email = "a@b.com" };
        spec.IsSatisfiedBy(match2).Should().BeTrue();

        // Adult but deleted
        var noMatch1 = new User { IsActive = false, Role = "User", Age = 25, IsDeleted = true, Email = "a@b.com" };
        spec.IsSatisfiedBy(noMatch1).Should().BeFalse();

        // Adult but no email
        var noMatch2 = new User { IsActive = false, Role = "User", Age = 25, IsDeleted = false, Email = "" };
        spec.IsSatisfiedBy(noMatch2).Should().BeFalse();

        // Minor non-admin
        var noMatch3 = new User { IsActive = false, Role = "User", Age = 15, IsDeleted = false, Email = "a@b.com" };
        spec.IsSatisfiedBy(noMatch3).Should().BeFalse();
    }

    [Fact]
    public void DeepComposition_ExpressionTreeIsValid()
    {
        // Ensure deep compositions produce valid expression trees for queryable use
        var spec = IsActive
            .And(IsAdmin)
            .Or(IsAdult)
            .And(Spec<User>.Where(u => !u.IsDeleted))
            .And(Spec<User>.Where(u => !string.IsNullOrEmpty(u.Email)));

        var expr = spec.ToExpression();
        expr.Parameters.Should().HaveCount(1);

        // Verify it works with IQueryable
        var users = new List<User>
        {
            new() { IsActive = true, Role = "Admin", Age = 15, IsDeleted = false, Email = "a@b.com" },
            new() { IsActive = false, Role = "User", Age = 25, IsDeleted = false, Email = "a@b.com" },
            new() { IsActive = false, Role = "User", Age = 15, IsDeleted = false, Email = "a@b.com" }
        }.AsQueryable();

        var result = users.Where(expr).ToList();
        result.Should().HaveCount(2);
    }

    [Fact]
    public void AndIf_LazyFactory_WhenTrue_InvokesFactory()
    {
        var factoryInvoked = false;
        var spec = IsActive.AndIf(true, () =>
        {
            factoryInvoked = true;
            return IsAdmin;
        });

        factoryInvoked.Should().BeTrue();
        spec.IsSatisfiedBy(new User { IsActive = true, Role = "Admin" }).Should().BeTrue();
        spec.IsSatisfiedBy(new User { IsActive = true, Role = "User" }).Should().BeFalse();
    }

    [Fact]
    public void AndIf_LazyFactory_WhenFalse_DoesNotInvokeFactory()
    {
        var factoryInvoked = false;
        var spec = IsActive.AndIf(false, () =>
        {
            factoryInvoked = true;
            return IsAdmin;
        });

        factoryInvoked.Should().BeFalse();
        spec.IsSatisfiedBy(new User { IsActive = true, Role = "User" }).Should().BeTrue();
    }

    [Fact]
    public void OrIf_LazyFactory_WhenTrue_InvokesFactory()
    {
        var factoryInvoked = false;
        var spec = IsAdmin.OrIf(true, () =>
        {
            factoryInvoked = true;
            return IsAdult;
        });

        factoryInvoked.Should().BeTrue();
        spec.IsSatisfiedBy(new User { Role = "User", Age = 25 }).Should().BeTrue();
    }

    [Fact]
    public void OrIf_LazyFactory_WhenFalse_DoesNotInvokeFactory()
    {
        var factoryInvoked = false;
        var spec = IsAdmin.OrIf(false, () =>
        {
            factoryInvoked = true;
            return IsAdult;
        });

        factoryInvoked.Should().BeFalse();
        spec.IsSatisfiedBy(new User { Role = "User", Age = 25 }).Should().BeFalse();
    }

    [Fact]
    public void ImplicitConversion_FromExpression_CreatesSpecification()
    {
        Expression<Func<User, bool>> expr = u => u.IsActive;
        Specification<User> spec = expr;

        spec.IsSatisfiedBy(new User { IsActive = true }).Should().BeTrue();
        spec.IsSatisfiedBy(new User { IsActive = false }).Should().BeFalse();
    }

    [Fact]
    public void ImplicitConversion_CanBeUsedInComposition()
    {
        Expression<Func<User, bool>> activeExpr = u => u.IsActive;
        Expression<Func<User, bool>> adminExpr = u => u.Role == "Admin";
        Specification<User> isActive = activeExpr;
        Specification<User> isAdmin = adminExpr;

        var combined = isActive.And(isAdmin);

        combined.IsSatisfiedBy(new User { IsActive = true, Role = "Admin" }).Should().BeTrue();
        combined.IsSatisfiedBy(new User { IsActive = true, Role = "User" }).Should().BeFalse();
    }

    [Fact]
    public void ToString_ComposedSpec_ReturnsReadableString()
    {
        var spec = IsActive.And(IsAdmin).Or(IsAdult);
        var str = spec.ToString();

        // Should contain AND and OR in the string representation
        str.Should().Contain("AND");
        str.Should().Contain("OR");
    }

    [Fact]
    public void ToString_True_ReturnsTrue()
    {
        Spec<User>.True.ToString().Should().Be("True");
    }

    [Fact]
    public void ToString_False_ReturnsFalse()
    {
        Spec<User>.False.ToString().Should().Be("False");
    }

    [Fact]
    public void ToString_Not_ReturnsNotString()
    {
        var spec = IsActive.Not();
        spec.ToString().Should().Contain("NOT");
    }

    [Fact]
    public void CachedExpression_ReturnsSameInstance()
    {
        // Verify that composed specs cache their expression
        var spec = IsActive.And(IsAdmin);
        var expr1 = spec.ToExpression();
        var expr2 = spec.ToExpression();

        expr1.Should().BeSameAs(expr2);
    }

    [Fact]
    public void And_WithISpecification_WrapsCorrectly()
    {
        // Create an ISpecification that is not a Specification<T>
        ISpecification<User> ispec = new CustomSpecification();
        var spec = IsActive.And(ispec);

        spec.IsSatisfiedBy(new User { IsActive = true, Role = "Admin" }).Should().BeTrue();
        spec.IsSatisfiedBy(new User { IsActive = true, Role = "User" }).Should().BeFalse();
    }

    [Fact]
    public void Or_WithISpecification_WrapsCorrectly()
    {
        ISpecification<User> ispec = new CustomSpecification();
        var spec = Spec<User>.False.Or(ispec);

        spec.IsSatisfiedBy(new User { Role = "Admin" }).Should().BeTrue();
        spec.IsSatisfiedBy(new User { Role = "User" }).Should().BeFalse();
    }

    /// <summary>
    ///     A custom ISpecification implementation that is NOT a Specification&lt;T&gt; subclass.
    ///     Used to test that composition correctly wraps ISpecification inputs.
    /// </summary>
    private sealed class CustomSpecification : ISpecification<User>
    {
        public System.Linq.Expressions.Expression<Func<User, bool>> ToExpression()
            => u => u.Role == "Admin";

        public bool IsSatisfiedBy(User entity) => entity.Role == "Admin";
    }
}
