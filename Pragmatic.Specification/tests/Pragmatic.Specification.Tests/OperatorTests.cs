using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class OperatorTests
{
    private static Specification<User> IsActive => Spec<User>.Where(u => u.IsActive);
    private static Specification<User> IsAdmin => Spec<User>.Where(u => u.Role == "Admin");
    private static Specification<User> IsAdult => Spec<User>.Where(u => u.Age >= 18);

    [Fact]
    public void AndOperator_CombinesSpecifications()
    {
        // Arrange
        var spec = IsActive & IsAdmin;
        var activeAdmin = new User { IsActive = true, Role = "Admin" };
        var activeUser = new User { IsActive = true, Role = "User" };

        // Act & Assert
        spec.IsSatisfiedBy(activeAdmin).Should().BeTrue();
        spec.IsSatisfiedBy(activeUser).Should().BeFalse();
    }

    [Fact]
    public void OrOperator_CombinesSpecifications()
    {
        // Arrange
        var spec = IsAdmin | IsAdult;
        var adminMinor = new User { Role = "Admin", Age = 15 };
        var userAdult = new User { Role = "User", Age = 25 };
        var userMinor = new User { Role = "User", Age = 15 };

        // Act & Assert
        spec.IsSatisfiedBy(adminMinor).Should().BeTrue();
        spec.IsSatisfiedBy(userAdult).Should().BeTrue();
        spec.IsSatisfiedBy(userMinor).Should().BeFalse();
    }

    [Fact]
    public void NotOperator_NegatesSpecification()
    {
        // Arrange
        var spec = !IsActive;
        var activeUser = new User { IsActive = true };
        var inactiveUser = new User { IsActive = false };

        // Act & Assert
        spec.IsSatisfiedBy(activeUser).Should().BeFalse();
        spec.IsSatisfiedBy(inactiveUser).Should().BeTrue();
    }

    [Fact]
    public void Operators_ChainCorrectly()
    {
        // (Active AND Admin) OR Adult
        var spec = (IsActive & IsAdmin) | IsAdult;

        var activeAdmin = new User { IsActive = true, Role = "Admin", Age = 15 };
        var inactiveAdult = new User { IsActive = false, Role = "User", Age = 25 };
        var inactiveMinor = new User { IsActive = false, Role = "User", Age = 15 };

        spec.IsSatisfiedBy(activeAdmin).Should().BeTrue();
        spec.IsSatisfiedBy(inactiveAdult).Should().BeTrue();
        spec.IsSatisfiedBy(inactiveMinor).Should().BeFalse();
    }
}