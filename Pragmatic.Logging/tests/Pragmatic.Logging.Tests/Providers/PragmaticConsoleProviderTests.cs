using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticConsoleProviderTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();

        // Act
        using var provider = new PragmaticConsoleProvider("TestConsole", config);

        // Assert
        provider.Name.Should().Be("TestConsole");
        provider.Configuration.Should().Be(config);
    }

    [Fact]
    public void Constructor_WithBatchingEnabled_ShouldThrowException()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.Performance.EnableBatching = true;

        // Act & Assert
        var act = () => new PragmaticConsoleProvider("TestConsole", config);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*should not use batching*");
    }

    [Fact]
    public void ForConsole_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Information);
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeTrue();

        // Console should not use batching
        config.Performance.EnableBatching.Should().BeFalse();
        config.Performance.UseZeroAllocation.Should().BeTrue();

        // Should exclude noisy context properties for console
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.Exclude);
        config.ContextFilter.PropertyNames.Should().Contain("MachineName");
        config.ContextFilter.PropertyNames.Should().Contain("ProcessId");

        // Console-optimized formatting
        config.Formatting.TimestampFormat.Should().Be("HH:mm:ss.fff");
        config.Formatting.UseUtcTimestamp.Should().BeFalse(); // Local time for console
        config.Formatting.MaxMessageLength.Should().Be(0); // No length limit
        config.Formatting.MessageTemplate.Should().Contain("{Timestamp}");
        config.Formatting.MessageTemplate.Should().Contain("{Level}");
        config.Formatting.MessageTemplate.Should().Contain("{Category}");
        config.Formatting.MessageTemplate.Should().Contain("{Message}");

        // Console-specific custom properties
        config.CustomProperties["UseColors"].Should().Be(true);
        config.CustomProperties["TruncateCategories"].Should().Be(true);
        config.CustomProperties["MaxCategoryLength"].Should().Be(40);
        config.CustomProperties["UseStdErrorForErrors"].Should().Be(false);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        using var provider = new PragmaticConsoleProvider("TestConsole", config);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_WithConfiguredLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.MinimumLevel = LogLevel.Warning;
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_WithCategorySpecificLevel_ShouldOverrideMinimumLevel()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.MinimumLevel = LogLevel.Warning;
        config.CategoryLevels["Test.Category"] = LogLevel.Debug;
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");
        var otherLogger = provider.CreateLogger("Other.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeTrue();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();

        otherLogger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        otherLogger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void GetMetrics_ShouldReturnConsoleSpecificMetrics()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        using var provider = new PragmaticConsoleProvider("TestConsole", config);

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("SupportsColors");
        metrics.CustomMetrics.Should().ContainKey("ConsoleWidth");
        metrics.CustomMetrics.Should().ContainKey("OutputEncoding");
        metrics.CustomMetrics.Should().ContainKey("IsOutputRedirected");
        metrics.CustomMetrics.Should().ContainKey("IsErrorRedirected");
    }

    [Fact]
    public void CheckHealth_ShouldReturnHealthyWhenConsoleAvailable()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        using var provider = new PragmaticConsoleProvider("TestConsole", config);

        // Act
        var health = provider.CheckHealth();

        // Assert
        health.Should().BeOneOf(ProviderHealthStatus.Healthy, ProviderHealthStatus.Degraded);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRCE")]
    [InlineData(LogLevel.Debug, "DBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARN")]
    [InlineData(LogLevel.Error, "FAIL")]
    [InlineData(LogLevel.Critical, "CRIT")]
    public void LogMessage_ShouldFormatLevelCorrectly(LogLevel logLevel, string expectedLevel)
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.MinimumLevel = LogLevel.Trace; // Enable all levels for testing
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");

        // Capture console output (in a real test, you might redirect Console.Out)
        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            // Act
            logger.Log(logLevel, "Test message");

            // Assert
            var output = consoleCapture.ToString();
            output.Should().Contain(expectedLevel);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldIncludeProperties()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            // Act
            logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");

            // Assert
            var output = consoleCapture.ToString();
            output.Should().Contain("UserId=12345");
            output.Should().Contain("Action=\"Login\"");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void LogMessage_WithException_ShouldIncludeExceptionDetails()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        config.Formatting.IncludeExceptionDetails = true;
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            // Act
            var exception = new InvalidOperationException("Test exception");
            logger.LogError(exception, "An error occurred");

            // Assert
            var output = consoleCapture.ToString();
            output.Should().Contain("Exception:");
            output.Should().Contain("InvalidOperationException");
            output.Should().Contain("Test exception");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateFormattingTemplate()
    {
        // Arrange
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        using var provider = new PragmaticConsoleProvider("TestConsole", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var newConfig = PragmaticConsoleConfiguration.ForAdvancedConsole();
        newConfig.Formatting.MessageTemplate = "[{Level}] {Message}"; // Simplified template
        provider.UpdateConfiguration(newConfig);

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            logger.LogInformation("Test message");

            // Assert
            var output = consoleCapture.ToString();
            output.Should().Contain("[INFO] Test message");
            output.Should().NotContain("Test.Category"); // Category should not be in simplified template
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    ///     A member a type declared must not be logged stays out of the console line: the message is
    ///     rendered from the masked value, not by the caller's formatter.
    /// </summary>
    /// <remarks>In this class because it redirects <see cref="Console.Out" />, as the tests above do.</remarks>
    [Fact]
    public void LogMessage_WithADeclaredMember_WritesItMasked()
    {
        var config = PragmaticConsoleConfiguration.ForAdvancedConsole();
        using var provider = new PragmaticConsoleProvider("TestConsole", config)
        {
            DeclaredRedactor = new Pragmatic.Redaction.DeclaredRedactor([new CredentialsMap()]),
        };
        var logger = provider.CreateLogger("Test.Category");

        using var consoleCapture = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(consoleCapture);

        try
        {
            logger.LogInformation("Signed in with {Credentials}", new Credentials("jane", "hunter2"));

            var output = consoleCapture.ToString();
            output.Should().NotContain("hunter2");
            output.Should().Contain(Pragmatic.Redaction.PersonalDataPatterns.Mask).And.Contain("jane");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private sealed record Credentials(string User, string Password);

    private sealed class CredentialsMap : Pragmatic.Serialization.IRedactionMap
    {
        public bool TryGetRedactedMembers(Type type, out IReadOnlyList<Pragmatic.Serialization.RedactedMember> members)
        {
            members = type == typeof(Credentials)
                ? [new Pragmatic.Serialization.RedactedMember(nameof(Credentials.Password), Pragmatic.Serialization.RedactionReason.NotLogged)]
                : [];
            return members.Count > 0;
        }
    }
}