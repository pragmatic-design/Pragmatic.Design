using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class SpecFactoryTests
{
    [Fact]
    public void Where_CreatesSpecificationFromExpression()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive);
        var activeUser = new User { IsActive = true };
        var inactiveUser = new User { IsActive = false };

        // Act & Assert
        spec.IsSatisfiedBy(activeUser).Should().BeTrue();
        spec.IsSatisfiedBy(inactiveUser).Should().BeFalse();
    }

    [Fact]
    public void Where_WithComplexExpression_Works()
    {
        // Arrange
        var spec = Spec<User>.Where(u => u.IsActive && u.Age >= 18);
        var adultActive = new User { IsActive = true, Age = 25 };
        var minorActive = new User { IsActive = true, Age = 15 };
        var adultInactive = new User { IsActive = false, Age = 25 };

        // Act & Assert
        spec.IsSatisfiedBy(adultActive).Should().BeTrue();
        spec.IsSatisfiedBy(minorActive).Should().BeFalse();
        spec.IsSatisfiedBy(adultInactive).Should().BeFalse();
    }

    [Fact]
    public void True_AlwaysReturnsTrue()
    {
        // Arrange
        var spec = Spec<User>.True;
        var user1 = new User { IsActive = true };
        var user2 = new User { IsActive = false };

        // Act & Assert
        spec.IsSatisfiedBy(user1).Should().BeTrue();
        spec.IsSatisfiedBy(user2).Should().BeTrue();
    }

    [Fact]
    public void True_IsSingletonInstance()
    {
        // Arrange & Act
        var spec1 = Spec<User>.True;
        var spec2 = Spec<User>.True;

        // Assert
        spec1.Should().BeSameAs(spec2);
    }

    [Fact]
    public void False_AlwaysReturnsFalse()
    {
        // Arrange
        var spec = Spec<User>.False;
        var user1 = new User { IsActive = true };
        var user2 = new User { IsActive = false };

        // Act & Assert
        spec.IsSatisfiedBy(user1).Should().BeFalse();
        spec.IsSatisfiedBy(user2).Should().BeFalse();
    }

    [Fact]
    public void False_IsSingletonInstance()
    {
        // Arrange & Act
        var spec1 = Spec<User>.False;
        var spec2 = Spec<User>.False;

        // Assert
        spec1.Should().BeSameAs(spec2);
    }
}