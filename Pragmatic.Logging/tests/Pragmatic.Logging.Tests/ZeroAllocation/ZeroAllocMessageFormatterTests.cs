using Pragmatic.Testing.Assertions;
using Pragmatic.Logging.ZeroAllocation;

namespace Pragmatic.Logging.Tests.ZeroAllocation;

public class ZeroAllocMessageFormatterTests
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
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("User 123 logged in");
    }

    [Fact]
    public void Format_WithMultipleProperties_ShouldReplaceAll()
    {
        // Arrange
        var template = "User {UserId} from {Location} processed {Count} items";
        var properties = Props(("UserId", "123"), ("Location", "NYC"), ("Count", 42));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("User 123 from NYC processed 42 items");
    }

    [Fact]
    public void Format_WithDateTimeProperty_ShouldFormatCorrectly()
    {
        // Arrange
        var template = "Event occurred at {Timestamp}";
        var timestamp = new DateTime(2024, 1, 15, 10, 30, 45);
        var properties = Props(("Timestamp", timestamp));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Contain("2024").And.Contain("10").And.Contain("30");
    }

    [Fact]
    public void Format_WithGuidProperty_ShouldFormatCorrectly()
    {
        // Arrange
        var template = "Session {SessionId} started";
        var sessionId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var properties = Props(("SessionId", sessionId));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("Session 12345678-1234-1234-1234-123456789abc started");
    }

    [Fact]
    public void Format_WithDecimalProperty_ShouldFormatCorrectly()
    {
        // Arrange
        var template = "Transaction amount: {Amount}";
        var properties = Props(("Amount", 123.45m));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("Transaction amount: 123.45");
    }

    [Fact]
    public void Format_WithNullProperty_ShouldShowNull()
    {
        // Arrange
        var template = "User {UserId} has location {Location}";
        var properties = Props(("UserId", "123"), ("Location", null));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("User 123 has location null");
    }

    [Fact]
    public void Format_WithEmptyTemplate_ShouldReturnEmpty()
    {
        // Arrange
        var template = "";
        var properties = Props(("UserId", "123"));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

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
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be(template);
    }

    [Fact]
    public void TryFormat_WithSufficientBuffer_ShouldSucceed()
    {
        // Arrange
        var template = "User {UserId} count {Count}";
        var parameters = new object?[] { "123", 42 };
        Span<char> buffer = stackalloc char[100];

        // Act
        var success = ZeroAllocMessageFormatter.TryFormat(template.AsSpan(), parameters, buffer, out int charsWritten);

        // Assert
        success.Should().BeTrue();
        charsWritten.Should().Be("User 123 count 42".Length);
        buffer[..charsWritten].ToString().Should().Be("User 123 count 42");
    }

    [Fact]
    public void TryFormat_WithInsufficientBuffer_ShouldFail()
    {
        // Arrange
        var template = "User {UserId} from {Location} with a very long message that exceeds buffer";
        var parameters = new object?[] { "123", "New York City" };
        Span<char> buffer = stackalloc char[10]; // Too small

        // Act
        var success = ZeroAllocMessageFormatter.TryFormat(template.AsSpan(), parameters, buffer, out int charsWritten);

        // Assert
        success.Should().BeFalse();
        charsWritten.Should().Be(0);
    }

    [Fact]
    public void TryFormat_WithEmptyTemplate_ShouldSucceed()
    {
        // Arrange
        var template = "";
        var parameters = new object?[] { "123" };
        Span<char> buffer = stackalloc char[10];

        // Act
        var success = ZeroAllocMessageFormatter.TryFormat(template.AsSpan(), parameters, buffer, out int charsWritten);

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
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Be("User {UserId is not closed");
    }

    [Fact]
    public void Format_WithPropertyFormat_ShouldApplyFormat()
    {
        // Arrange
        var template = "Amount: {Amount:C}";
        var properties = Props(("Amount", 123.45m));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Contain("123").And.Contain("45");
    }

    [Fact]
    public void Format_WithLargeMessage_ShouldUseStringBuilder()
    {
        // Arrange - Create a template that will exceed stack allocation threshold
        var template = string.Join(" ", Enumerable.Repeat("User {UserId} from {Location}", 60));
        var properties = Props(("UserId", "123"), ("Location", "NYC"));

        // Act
        var result = ZeroAllocMessageFormatter.Format(template.AsSpan(), properties);

        // Assert
        result.Should().Contain("User 123 from NYC");
        result.Length.Should().BeGreaterThan(1000); // Should be a large message
    }
}
