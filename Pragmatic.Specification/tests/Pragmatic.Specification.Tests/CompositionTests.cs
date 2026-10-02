using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class CompositionTests
{
    private static Specification<User> IsActive => Spec<User>.Where(u => u.IsActive);
    private static Specification<User> IsNotDeleted => Spec<User>.Where(u => !u.IsDeleted);
    private static Specification<User> IsAdmin => Spec<User>.Where(u => u.Role == "Admin");
    private static Specification<User> IsAdult => Spec<User>.Where(u => u.Age >= 18);

    [Fact]
    public void And_CombinesSpecifications()
    {
        // Arrange
        var spec = IsActive.And(IsNotDeleted);
        var activeNotDeleted = new User { IsActive = true, IsDeleted = false };
        var activeDeleted = new User { IsActive = true, IsDeleted = true };
        var inactiveNotDeleted = new User { IsActive = false, IsDeleted = false };

        // Act & Assert
        spec.IsSatisfiedBy(activeNotDeleted).Should().BeTrue();
        spec.IsSatisfiedBy(activeDeleted).Should().BeFalse();
        spec.IsSatisfiedBy(inactiveNotDeleted).Should().BeFalse();
    }

    [Fact]
    public void Or_CombinesSpecifications()
    {
        // Arrange
        var spec = IsAdmin.Or(IsAdult);
        var adminMinor = new User { Role = "Admin", Age = 15 };
        var userAdult = new User { Role = "User", Age = 25 };
        var userMinor = new User { Role = "User", Age = 15 };

        // Act & Assert
        spec.IsSatisfiedBy(adminMinor).Should().BeTrue();
        spec.IsSatisfiedBy(userAdult).Should().BeTrue();
        spec.IsSatisfiedBy(userMinor).Should().BeFalse();
    }

    [Fact]
    public void Not_NegatesSpecification()
    {
        // Arrange
        var spec = IsActive.Not();
        var activeUser = new User { IsActive = true };
        var inactiveUser = new User { IsActive = false };

        // Act & Assert
        spec.IsSatisfiedBy(activeUser).Should().BeFalse();
        spec.IsSatisfiedBy(inactiveUser).Should().BeTrue();
    }

    [Fact]
    public void ComplexComposition_Works()
    {
        // Active admin or adult non-deleted user
        var spec = IsActive.And(IsAdmin)
            .Or(IsAdult.And(IsNotDeleted));

        var activeAdmin = new User { IsActive = true, Role = "Admin", Age = 15, IsDeleted = false };
        var adultUser = new User { IsActive = false, Role = "User", Age = 25, IsDeleted = false };
        var deletedAdult = new User { IsActive = false, Role = "User", Age = 25, IsDeleted = true };
        var inactiveMinor = new User { IsActive = false, Role = "User", Age = 15, IsDeleted = false };

        spec.IsSatisfiedBy(activeAdmin).Should().BeTrue();
        spec.IsSatisfiedBy(adultUser).Should().BeTrue();
        spec.IsSatisfiedBy(deletedAdult).Should().BeFalse();
        spec.IsSatisfiedBy(inactiveMinor).Should().BeFalse();
    }

    [Fact]
    public void AndIf_WhenConditionTrue_CombinesSpecifications()
    {
        // Arrange
        var spec = IsActive.AndIf(true, IsAdmin);
        var activeAdmin = new User { IsActive = true, Role = "Admin" };
        var activeUser = new User { IsActive = true, Role = "User" };

        // Act & Assert
        spec.IsSatisfiedBy(activeAdmin).Should().BeTrue();
        spec.IsSatisfiedBy(activeUser).Should().BeFalse();
    }

    [Fact]
    public void AndIf_WhenConditionFalse_ReturnsOriginalSpec()
    {
        // Arrange
        var spec = IsActive.AndIf(false, IsAdmin);
        var activeUser = new User { IsActive = true, Role = "User" };

        // Act & Assert - should only check IsActive
        spec.IsSatisfiedBy(activeUser).Should().BeTrue();
    }

    [Fact]
    public void OrIf_WhenConditionTrue_CombinesSpecifications()
    {
        // Arrange
        var spec = IsAdmin.OrIf(true, IsAdult);
        var userAdult = new User { Role = "User", Age = 25 };
        var userMinor = new User { Role = "User", Age = 15 };

        // Act & Assert
        spec.IsSatisfiedBy(userAdult).Should().BeTrue();
        spec.IsSatisfiedBy(userMinor).Should().BeFalse();
    }

    [Fact]
    public void OrIf_WhenConditionFalse_ReturnsOriginalSpec()
    {
        // Arrange
        var spec = IsAdmin.OrIf(false, IsAdult);
        var userAdult = new User { Role = "User", Age = 25 };

        // Act & Assert - should only check IsAdmin
        spec.IsSatisfiedBy(userAdult).Should().BeFalse();
    }
}