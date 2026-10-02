using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests;

public class LogPropertyTests
{
    [Fact]
    public void LogProperty_ShouldCreateWithCorrectValues()
    {
        // Arrange
        var name = "UserId";
        var value = "123";

        // Act
        var property = new LogProperty<string>(name, value);

        // Assert
        property.Name.Should().Be(name);
        property.Value.Should().Be(value);
        property.ValueType.Should().Be(typeof(string));
        property.Destructure.Should().BeFalse();
    }

    [Fact]
    public void LogProperty_WithDestructure_ShouldSetFlag()
    {
        // Arrange
        var name = "User";
        var value = new { Id = "123", Name = "John" };

        // Act
        var property = new LogProperty<object>(name, value, destructure: true);

        // Assert
        property.Name.Should().Be(name);
        property.Value.Should().Be(value);
        property.Destructure.Should().BeTrue();
    }

    [Fact]
    public void LogProperty_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var property = new LogProperty<int>("Count", 42);

        // Act
        var result = property.ToString();

        // Assert
        result.Should().Be("Count = 42");
    }

    [Fact]
    public void LogProperty_Equals_ShouldWorkCorrectly()
    {
        // Arrange
        var prop1 = new LogProperty<string>("Name", "John");
        var prop2 = new LogProperty<string>("Name", "John");
        var prop3 = new LogProperty<string>("Name", "Jane");

        // Act & Assert
        prop1.Should().Be(prop2);
        prop1.Should().NotBe(prop3);
        (prop1 == prop2).Should().BeTrue();
        (prop1 != prop3).Should().BeTrue();
    }

    [Fact]
    public void LogProperty_GetHashCode_ShouldBeConsistent()
    {
        // Arrange
        var prop1 = new LogProperty<string>("Name", "John");
        var prop2 = new LogProperty<string>("Name", "John");

        // Act & Assert
        prop1.GetHashCode().Should().Be(prop2.GetHashCode());
    }

    [Fact]
    public void LogProperty_Factory_Create_ShouldWork()
    {
        // Arrange
        var name = "Temperature";
        var value = 25.5m;

        // Act
        var property = LogProperty.Create(name, value);

        // Assert
        property.Name.Should().Be(name);
        property.Value.Should().Be(value);
        property.ValueType.Should().Be(typeof(decimal));
        property.Destructure.Should().BeFalse();
    }

    [Fact]
    public void LogProperty_Factory_Destructure_ShouldSetFlag()
    {
        // Arrange
        var name = "Settings";
        var value = new { Theme = "Dark", Language = "en" };

        // Act
        var property = LogProperty.Destructure(name, value);

        // Assert
        property.Name.Should().Be(name);
        property.Value.Should().Be(value);
        property.Destructure.Should().BeTrue();
    }

    [Fact]
    public void LogProperty_FromKeyValuePair_ShouldWork()
    {
        // Arrange
        var kvp = new KeyValuePair<string, int>("Count", 100);

        // Act
        var property = LogProperty.FromKeyValuePair(kvp);

        // Assert
        property.Name.Should().Be("Count");
        property.Value.Should().Be(100);
        property.ValueType.Should().Be(typeof(int));
    }
}