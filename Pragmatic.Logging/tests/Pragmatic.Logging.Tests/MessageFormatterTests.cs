using Pragmatic.Testing.Assertions;

namespace Pragmatic.Logging.Tests;

public class MessageFormatterTests
{
    private static KeyValuePair<string, object?>[] Props(params (string Key, object? Value)[] items)
        => items.Select(x => new KeyValuePair<string, object?>(x.Key, x.Value)).ToArray();

    [Fact]
    public void Format_WithSimpleProperty_ShouldReplaceCorrectly()
    {
        // Arrange
        var template = "User {UserId} logged in";
        var properties = Props(("UserId", "123"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Be("User 123 logged in");
    }

    [Fact]
    public void Format_WithMultipleProperties_ShouldReplaceAll()
    {
        // Arrange
        var template = "User {UserId} from {Location} at {Time}";
        var properties = Props(("UserId", "123"), ("Location", "NYC"), ("Time", "10:30"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Be("User 123 from NYC at 10:30");
    }

    [Fact]
    public void Format_WithMissingProperty_ShouldKeepPlaceholder()
    {
        // Arrange
        var template = "User {UserId} has {MissingProperty}";
        var properties = Props(("UserId", "123"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert - Missing property keeps placeholder since it's not in the KVP list
        result.Should().Be("User 123 has {MissingProperty}");
    }

    [Fact]
    public void Format_WithNullProperty_ShouldShowNull()
    {
        // Arrange
        var template = "User {UserId} has name {Name}";
        var properties = Props(("UserId", "123"), ("Name", null));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Be("User 123 has name null");
    }

    [Fact]
    public void Format_WithEmptyTemplate_ShouldReturnEmpty()
    {
        // Arrange
        var template = "";
        var properties = Props(("UserId", "123"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Format_WithNoPlaceholders_ShouldReturnTemplate()
    {
        // Arrange
        var template = "This is a simple message";
        var properties = Props(("UserId", "123"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Be(template);
    }

    [Fact]
    public void TryFormat_WithSufficientBuffer_ShouldSucceed()
    {
        // Arrange
        var template = "User {UserId}";
        var properties = Props(("UserId", "123"));
        Span<char> buffer = stackalloc char[50];

        // Act
        var success = MessageFormatter.TryFormat(template, properties, buffer, out int charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().Be("User 123".Length);
        buffer[..charsWritten].ToString().Should().Be("User 123");
    }

    [Fact]
    public void TryFormat_WithInsufficientBuffer_ShouldFail()
    {
        // Arrange
        var template = "User {UserId} with a very long message";
        var properties = Props(("UserId", "123"));
        Span<char> buffer = stackalloc char[5];

        // Act
        var success = MessageFormatter.TryFormat(template, properties, buffer, out int charsWritten);

        // Assert
        success.Should().BeFalse();
        charsWritten.Should().Be(0);
    }

    [Fact]
    public void TryFormat_WithEmptyTemplate_ShouldSucceed()
    {
        // Arrange
        var template = "";
        var properties = Props(("UserId", "123"));
        Span<char> buffer = stackalloc char[10];

        // Act
        var success = MessageFormatter.TryFormat(template, properties, buffer, out int charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().Be(0);
    }

    [Fact]
    public void Format_WithMalformedPlaceholder_ShouldHandleGracefully()
    {
        // Arrange
        var template = "User {UserId is not closed";
        var properties = Props(("UserId", "123"));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Be("User {UserId is not closed");
    }

    [Fact]
    public void Format_WithComplexObject_ShouldFormatToString()
    {
        // Arrange
        var template = "Processing {Request}";
        var request = new { Id = 1, Name = "Test" };
        var properties = Props(("Request", request));

        // Act
        var result = MessageFormatter.Format(template, properties);

        // Assert
        result.Should().Contain("Processing");
        result.Should().Contain("Id");
        result.Should().Contain("Name");
    }
}
