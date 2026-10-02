using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Logging.Tests;

public class LogMessageTests
{
    private static KeyValuePair<string, object?>[] Props(params (string Key, object? Value)[] items)
        => items.Select(x => new KeyValuePair<string, object?>(x.Key, x.Value)).ToArray();

    [Fact]
    public void LogMessage_ShouldCreateWithCorrectProperties()
    {
        // Arrange
        var logLevel = LogLevel.Information;
        var template = "User {UserId} logged in";
        var properties = Props(("UserId", "123"));
        var eventId = new EventId(1, "UserLogin");

        // Act
        var message = new LogMessage<object>(logLevel, template, properties, eventId);

        // Assert
        message.LogLevel.Should().Be(logLevel);
        message.Template.Should().Be(template);
        message.Properties.Should().BeSameAs(properties);
        message.EventId.Should().Be(eventId);
        message.Exception.Should().BeNull();
    }

    [Fact]
    public void LogMessage_ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var template = "User {UserId} logged in from {Location}";
        var properties = Props(("UserId", "123"), ("Location", "New York"));
        var message = new LogMessage<object>(LogLevel.Information, template, properties);

        // Act
        var result = message.ToString();

        // Assert
        result.Should().Be("User 123 logged in from New York");
    }

    [Fact]
    public void LogMessage_Factory_Create_ShouldWork()
    {
        // Arrange
        var template = "Processing {Count} items";
        var properties = Props(("Count", 42));

        // Act
        var message = LogMessage.Create(LogLevel.Debug, template, properties);

        // Assert
        message.LogLevel.Should().Be(LogLevel.Debug);
        message.Template.Should().Be(template);
        message.Properties.Should().BeSameAs(properties);
    }

    [Fact]
    public void LogMessage_Information_ShouldCreateInformationMessage()
    {
        // Arrange
        var template = "Operation completed";
        var properties = Props(("Success", true));

        // Act
        var message = LogMessage.Information(template, properties);

        // Assert
        message.LogLevel.Should().Be(LogLevel.Information);
        message.Template.Should().Be(template);
        message.Properties.Should().BeSameAs(properties);
    }

    [Fact]
    public void LogMessage_Error_ShouldCreateErrorMessageWithException()
    {
        // Arrange
        var template = "Operation failed";
        var properties = Props(("OperationId", "op-123"));
        var exception = new InvalidOperationException("Test exception");

        // Act
        var message = LogMessage.Error(template, properties, exception: exception);

        // Assert
        message.LogLevel.Should().Be(LogLevel.Error);
        message.Template.Should().Be(template);
        message.Properties.Should().BeSameAs(properties);
        message.Exception.Should().Be(exception);
    }

    [Fact]
    public void LogMessage_TryFormat_ShouldSucceedWithSufficientBuffer()
    {
        // Arrange
        var template = "User {UserId}";
        var properties = Props(("UserId", "123"));
        var message = new LogMessage<object>(LogLevel.Information, template, properties);
        Span<char> buffer = stackalloc char[100];

        // Act
        var success = message.TryFormat(buffer, out int charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().Be("User 123".Length);
        buffer[..charsWritten].ToString().Should().Be("User 123");
    }

    [Fact]
    public void LogMessage_TryFormat_ShouldFailWithInsufficientBuffer()
    {
        // Arrange
        var template = "User {UserId} with a very long message";
        var properties = Props(("UserId", "123"));
        var message = new LogMessage<object>(LogLevel.Information, template, properties);
        Span<char> buffer = stackalloc char[5]; // Too small

        // Act
        var success = message.TryFormat(buffer, out int charsWritten);

        // Assert
        success.Should().BeFalse();
        charsWritten.Should().Be(0);
    }
}
