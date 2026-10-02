using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticDebugProviderTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();

        // Act
        using var provider = new PragmaticDebugProvider("TestDebug", config);

        // Assert
        provider.Name.Should().Be("TestDebug");
        provider.Configuration.Should().Be(config);
    }

    [Fact]
    public void ForDebug_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticDebugConfiguration.ForDebug();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Trace); // Capture everything for debugging
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeFalse(); // Reduce noise

        // Debug should not use batching for immediate output
        config.Performance.EnableBatching.Should().BeFalse();
        config.Performance.UseZeroAllocation.Should().BeFalse();

        // Debug-optimized formatting
        config.Formatting.TimestampFormat.Should().Be("HH:mm:ss.fff");
        config.Formatting.UseUtcTimestamp.Should().BeFalse(); // Local time for debugging
        config.Formatting.MaxMessageLength.Should().Be(0); // No limit

        // Debug-specific custom properties
        config.CustomProperties["IncludeTimestamp"].Should().Be(true);
        config.CustomProperties["IncludeThreadInfo"].Should().Be(true);
        config.CustomProperties["IncludeCategory"].Should().Be(true);
        config.CustomProperties["CategoryFilter"].Should().Be(string.Empty);
    }

    [Fact]
    public void ForFocusedDebug_ShouldCreateFocusedConfiguration()
    {
        // Act
        var config = PragmaticDebugConfiguration.ForFocusedDebug("MyComponent");

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Debug); // Less verbose for focused debugging
        config.CustomProperties["CategoryFilter"].Should().Be("MyComponent");
    }

    [Fact]
    public void ForLightweightDebug_ShouldCreateLightweightConfiguration()
    {
        // Act
        var config = PragmaticDebugConfiguration.ForLightweightDebug();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Information); // Less verbose
        config.IncludeStructuredProperties.Should().BeFalse(); // Reduce output
        config.CustomProperties["IncludeThreadInfo"].Should().Be(false);
        config.CustomProperties["IncludeTimestamp"].Should().Be(false);
    }

    [Fact]
    public void ForPerformanceDebug_ShouldCreatePerformanceConfiguration()
    {
        // Act
        var config = PragmaticDebugConfiguration.ForPerformanceDebug();

        // Assert
        config.Formatting.TimestampFormat.Should().Be("HH:mm:ss.ffffff"); // Microsecond precision
        config.CustomProperties["IncludeThreadInfo"].Should().Be(true);
        config.ContextFilter.PropertyNames.Should().Contain("Duration");
        config.ContextFilter.PropertyNames.Should().Contain("ExecutionTime");
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_WithConfiguredLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        config.MinimumLevel = LogLevel.Warning;
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        logger.IsEnabled(LogLevel.Information).Should().BeFalse();
        logger.IsEnabled(LogLevel.Warning).Should().BeTrue();
        logger.IsEnabled(LogLevel.Error).Should().BeTrue();
        logger.IsEnabled(LogLevel.Critical).Should().BeTrue();
    }

    [Fact]
    public void LogMessage_ShouldWriteToDebugOutput()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        // Capture debug output using a custom TraceListener
        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.LogInformation("Test debug message");

            // Allow some time for the message to be processed
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = capturedOutput.FirstOrDefault();
            debugMessage.Should().NotBeNull();
            debugMessage.Should().Contain("Test debug message");
            debugMessage.Should().Contain("[INFO]");
            debugMessage.Should().Contain("Test.Category");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldIncludeProperties()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = capturedOutput.FirstOrDefault();
            debugMessage.Should().Contain("UserId=12345");
            debugMessage.Should().Contain("Action=\"Login\"");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void LogMessage_WithException_ShouldIncludeExceptionDetails()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        config.Formatting.IncludeExceptionDetails = true;
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            var exception = new InvalidOperationException("Test exception");
            logger.LogError(exception, "An error occurred");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = string.Join("\n", capturedOutput);
            debugMessage.Should().Contain("An error occurred");
            debugMessage.Should().Contain("Exception:");
            debugMessage.Should().Contain("InvalidOperationException");
            debugMessage.Should().Contain("Test exception");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void LogMessage_WithCategoryFilter_ShouldFilterCorrectly()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForFocusedDebug("MyComponent");
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var matchingLogger = provider.CreateLogger("MyComponent.Service");
        var nonMatchingLogger = provider.CreateLogger("OtherComponent.Service");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            matchingLogger.LogInformation("This should appear");
            nonMatchingLogger.LogInformation("This should not appear");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().HaveCount(1);
            capturedOutput[0].Should().Contain("This should appear");
            capturedOutput[0].Should().NotContain("This should not appear");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void GetMetrics_ShouldReturnDebugSpecificMetrics()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("DebuggerAttached");
        metrics.CustomMetrics.Should().ContainKey("IsDebugBuild");
        metrics.CustomMetrics.Should().ContainKey("DebuggerAttachedMetrics");
        metrics.CustomMetrics.Should().ContainKey("IncludeTimestamp");
        metrics.CustomMetrics.Should().ContainKey("IncludeThreadInfo");
        metrics.CustomMetrics.Should().ContainKey("IncludeCategory");
        metrics.CustomMetrics.Should().ContainKey("CategoryFilter");
        metrics.CustomMetrics.Should().ContainKey("OutputType");
        metrics.CustomMetrics.Should().ContainKey("ConditionalCompilation");

        metrics.CustomMetrics["OutputType"].Should().Be("Debug");
        metrics.CustomMetrics["IncludeTimestamp"].Should().Be(true);
        metrics.CustomMetrics["IncludeThreadInfo"].Should().Be(true);
        metrics.CustomMetrics["IncludeCategory"].Should().Be(true);
        metrics.CustomMetrics["CategoryFilter"].Should().Be(string.Empty);
    }

    [Fact]
    public void CheckHealth_ShouldReturnAppropriateStatus()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);

        // Act
        var health = provider.CheckHealth();

        // Assert
        // Health status depends on whether debugger is attached and build configuration
        health.Should().BeOneOf(ProviderHealthStatus.Healthy, ProviderHealthStatus.Warning);
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
        var config = PragmaticDebugConfiguration.ForDebug();
        config.MinimumLevel = LogLevel.Trace; // Enable all levels for testing
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.Log(logLevel, "Test message");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = capturedOutput.FirstOrDefault();
            debugMessage.Should().Contain($"[{expectedLevel}]");
            debugMessage.Should().Contain("Test message");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void LogMessage_WithTimestampDisabled_ShouldNotIncludeTimestamp()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForLightweightDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.LogInformation("Test message");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = capturedOutput.FirstOrDefault();
            debugMessage.Should().NotContain("[2"); // Should not contain timestamp pattern
            debugMessage.Should().Contain("Test message");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public void LogMessage_WithCategoryDisabled_ShouldNotIncludeCategory()
    {
        // Arrange
        var config = PragmaticDebugConfiguration.ForLightweightDebug();
        using var provider = new PragmaticDebugProvider("TestDebug", config);
        var logger = provider.CreateLogger("Test.Category");

        var capturedOutput = new List<string>();
        var listener = new TestTraceListener(capturedOutput);
        Trace.Listeners.Add(listener);

        try
        {
            // Act
            logger.LogInformation("Test message");
            Thread.Sleep(10);

            // Assert
            capturedOutput.Should().NotBeEmpty();
            var debugMessage = capturedOutput.FirstOrDefault();
            debugMessage.Should().NotContain("Test.Category:");
            debugMessage.Should().Contain("Test message");
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
}

/// <summary>
/// Custom TraceListener for capturing debug output in tests.
/// </summary>
internal sealed class TestTraceListener(List<string> capturedOutput) : TraceListener
{
    public override void Write(string? message)
    {
        if (!string.IsNullOrEmpty(message))
        {
            lock (capturedOutput)
            {
                capturedOutput.Add(message);
            }
        }
    }

    public override void WriteLine(string? message)
    {
        Write(message);
    }
}
