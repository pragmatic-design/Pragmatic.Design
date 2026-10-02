using Pragmatic.Testing.Assertions;

namespace Pragmatic.Specification.Tests;

public class ExtensionTests
{
    private readonly List<User> _users =
    [
        new() { Id = 1, Name = "Alice", IsActive = true, Role = "Admin", Age = 30 },
        new() { Id = 2, Name = "Bob", IsActive = true, Role = "User", Age = 25 },
        new() { Id = 3, Name = "Charlie", IsActive = false, Role = "User", Age = 35 },
        new() { Id = 4, Name = "Diana", IsActive = true, Role = "Admin", Age = 17 },
        new() { Id = 5, Name = "Eve", IsActive = false, Role = "Admin", Age = 40 }
    ];

    private static Specification<User> IsActive => Spec<User>.Where(u => u.IsActive);
    private static Specification<User> IsAdmin => Spec<User>.Where(u => u.Role == "Admin");
    private static Specification<User> IsAdult => Spec<User>.Where(u => u.Age >= 18);

    #region IEnumerable Extensions

    [Fact]
    public void Where_OnEnumerable_FiltersCorrectly()
    {
        // Arrange & Act
        var result = _users.Where(IsActive).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.All(u => u.IsActive).Should().BeTrue();
    }

    [Fact]
    public void Where_WithComposedSpec_FiltersCorrectly()
    {
        // Arrange
        var spec = IsActive.And(IsAdmin);

        // Act
        var result = _users.Where(spec).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(u => u.Name == "Alice");
        result.Should().Contain(u => u.Name == "Diana");
    }

    [Fact]
    public void Any_WhenMatchExists_ReturnsTrue()
    {
        // Arrange & Act & Assert
        _users.Any(IsAdmin).Should().BeTrue();
    }

    [Fact]
    public void Any_WhenNoMatch_ReturnsFalse()
    {
        // Arrange
        var isSuperAdmin = Spec<User>.Where(u => u.Role == "SuperAdmin");

        // Act & Assert
        _users.Any(isSuperAdmin).Should().BeFalse();
    }

    [Fact]
    public void All_WhenAllMatch_ReturnsTrue()
    {
        // Arrange
        var hasName = Spec<User>.Where(u => !string.IsNullOrEmpty(u.Name));

        // Act & Assert
        _users.All(hasName).Should().BeTrue();
    }

    [Fact]
    public void All_WhenNotAllMatch_ReturnsFalse()
    {
        // Act & Assert
        _users.All(IsActive).Should().BeFalse();
    }

    [Fact]
    public void FirstOrDefault_WhenMatchExists_ReturnsFirst()
    {
        // Arrange & Act
        var result = _users.FirstOrDefault(IsAdmin);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
    }

    [Fact]
    public void FirstOrDefault_WhenNoMatch_ReturnsNull()
    {
        // Arrange
        var isSuperAdmin = Spec<User>.Where(u => u.Role == "SuperAdmin");

        // Act
        var result = _users.FirstOrDefault(isSuperAdmin);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void SingleOrDefault_WhenSingleMatch_ReturnsElement()
    {
        // Arrange
        var isCharlie = Spec<User>.Where(u => u.Name == "Charlie");

        // Act
        var result = _users.SingleOrDefault(isCharlie);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(3);
    }

    [Fact]
    public void SingleOrDefault_WhenMultipleMatches_Throws()
    {
        // Arrange & Act
        var act = () => _users.SingleOrDefault(IsAdmin);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Count_ReturnsCorrectCount()
    {
        // Arrange & Act
        var activeCount = _users.Count(IsActive);
        var adminCount = _users.Count(IsAdmin);

        // Assert
        activeCount.Should().Be(3);
        adminCount.Should().Be(3);
    }

    #endregion

    #region IQueryable Extensions

    [Fact]
    public void Where_OnQueryable_FiltersCorrectly()
    {
        // Arrange
        var queryable = _users.AsQueryable();

        // Act
        var result = queryable.Where(IsActive).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.All(u => u.IsActive).Should().BeTrue();
    }

    [Fact]
    public void Any_OnQueryable_Works()
    {
        // Arrange
        var queryable = _users.AsQueryable();

        // Act & Assert
        queryable.Any(IsAdmin).Should().BeTrue();
    }

    [Fact]
    public void All_OnQueryable_Works()
    {
        // Arrange
        var queryable = _users.AsQueryable();
        var hasName = Spec<User>.Where(u => !string.IsNullOrEmpty(u.Name));

        // Act & Assert
        queryable.All(hasName).Should().BeTrue();
        queryable.All(IsActive).Should().BeFalse();
    }

    [Fact]
    public void FirstOrDefault_OnQueryable_Works()
    {
        // Arrange
        var queryable = _users.AsQueryable();

        // Act
        var result = queryable.FirstOrDefault(IsAdmin);

        // Assert
        result.Should().NotBeNull();
        result!.Role.Should().Be("Admin");
    }

    [Fact]
    public void Count_OnQueryable_Works()
    {
        // Arrange
        var queryable = _users.AsQueryable();

        // Act
        var count = queryable.Count(IsActive.And(IsAdmin));

        // Assert
        count.Should().Be(2);
    }

    #endregion
}