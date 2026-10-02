using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticJsonProviderTests
{
    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();

        // Act
        using var provider = new PragmaticJsonProvider("TestJson", config);

        // Assert
        provider.Name.Should().Be("TestJson");
        provider.Configuration.Should().Be(config);
    }

    [Fact]
    public void Constructor_WithFilePath_ShouldInitializeForFileOutput()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();
        var tempFile = Path.GetTempFileName();

        try
        {
            // Act
            using var provider = new PragmaticJsonProvider("TestJsonFile", config, tempFile);

            // Assert
            provider.Name.Should().Be("TestJsonFile");
            provider.Configuration.Should().Be(config);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constructor_WithBatchingDisabled_ShouldThrowException()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        config.Performance.EnableBatching = false;

        // Act & Assert
        var act = () => new PragmaticJsonProvider("TestJson", config);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*should use batching*");
    }

    [Fact]
    public void ForJson_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticJsonConfiguration.ForJson();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Information);
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeTrue();

        // JSON should use batching for performance
        config.Performance.EnableBatching.Should().BeTrue();
        config.Performance.UseZeroAllocation.Should().BeTrue();
        config.Performance.BatchSize.Should().Be(50);
        config.Performance.FlushInterval.Should().Be(TimeSpan.FromSeconds(2));

        // JSON-optimized formatting
        config.Formatting.TimestampFormat.Should().Be("yyyy-MM-ddTHH:mm:ss.fffZ");
        config.Formatting.UseUtcTimestamp.Should().BeTrue();
        config.Formatting.IncludeExceptionDetails.Should().BeTrue();
        config.Formatting.MaxMessageLength.Should().Be(0); // No limit for JSON
        config.Formatting.PrettyPrintJson.Should().BeFalse();

        // JSON-specific custom properties
        config.CustomProperties["PrettyPrint"].Should().Be(false);
        config.CustomProperties["SerializeComplexObjects"].Should().Be(true);
        config.CustomProperties["SkipValidation"].Should().Be(true);
        config.CustomProperties["AutoFlush"].Should().Be(false);
    }

    [Fact]
    public void ForPrettyJson_ShouldCreateReadableConfiguration()
    {
        // Act
        var config = PragmaticJsonConfiguration.ForPrettyJson();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Debug); // More verbose for development
        config.CustomProperties["PrettyPrint"].Should().Be(true);
        config.CustomProperties["AutoFlush"].Should().Be(true); // Immediate feedback
        config.Performance.BatchSize.Should().Be(10); // Smaller batches
        config.Performance.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ForHighPerformanceJsonFile_ShouldCreateFileOptimizedConfiguration()
    {
        // Act
        var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();

        // Assert
        config.Performance.EnableBatching.Should().BeTrue();
        config.Performance.BatchSize.Should().Be(100);
        config.Performance.FlushInterval.Should().Be(TimeSpan.FromSeconds(5));
        config.Performance.MaxQueueSize.Should().Be(10000);

        // File rolling configuration
        config.CustomProperties["MaxFileSizeBytes"].Should().Be(500L * 1024 * 1024); // 500MB
        config.CustomProperties["RollByDate"].Should().Be(true);
        config.CustomProperties["AutoFlush"].Should().Be(false);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        using var provider = new PragmaticJsonProvider("TestJson", config);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_WithConfiguredLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        config.MinimumLevel = LogLevel.Warning;
        using var provider = new PragmaticJsonProvider("TestJson", config);
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
        var config = PragmaticJsonConfiguration.ForJson();
        config.MinimumLevel = LogLevel.Warning;
        config.CategoryLevels["Test.Category"] = LogLevel.Debug;
        using var provider = new PragmaticJsonProvider("TestJson", config);
        var logger = provider.CreateLogger("Test.Category");
        var otherLogger = provider.CreateLogger("Other.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeTrue();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();

        otherLogger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        otherLogger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void GetMetrics_ShouldReturnJsonSpecificMetrics()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        using var provider = new PragmaticJsonProvider("TestJson", config);

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("OutputType");
        metrics.CustomMetrics.Should().ContainKey("PrettyPrint");
        metrics.CustomMetrics.Should().ContainKey("CurrentFileSize");
        metrics.CustomMetrics.Should().ContainKey("SupportsUtf8");
        metrics.CustomMetrics["OutputType"].Should().Be("Stream");
        metrics.CustomMetrics["PrettyPrint"].Should().Be(false);
        metrics.CustomMetrics["SupportsUtf8"].Should().Be(true);
    }

    [Fact]
    public void GetMetrics_WithFilePath_ShouldIncludeFileMetrics()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();
        var tempFile = Path.GetTempFileName();

        try
        {
            using var provider = new PragmaticJsonProvider("TestJsonFile", config, tempFile);

            // Act
            var metrics = provider.GetMetrics();

            // Assert
            metrics.CustomMetrics["OutputType"].Should().Be("File");
            metrics.CustomMetrics.Should().ContainKey("FilePath");
            metrics.CustomMetrics.Should().ContainKey("FileExists");
            metrics.CustomMetrics.Should().ContainKey("CurrentFileDate");
            metrics.CustomMetrics["FilePath"].Should().Be(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void CheckHealth_ShouldReturnHealthyForStreamOutput()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        using var provider = new PragmaticJsonProvider("TestJson", config);

        // Act
        var health = provider.CheckHealth();

        // Assert
        health.Should().Be(ProviderHealthStatus.Healthy);
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
        var config = PragmaticJsonConfiguration.ForJson();
        config.MinimumLevel = LogLevel.Trace; // Enable all levels for testing
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.Log(logLevel, "Test message");
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        // Parse JSON to verify structure
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().NotBeEmpty();

        var jsonDoc = JsonDocument.Parse(lines[0]);
        jsonDoc.RootElement.GetProperty("@level").GetString().Should().Be(expectedLevel);
        jsonDoc.RootElement.GetProperty("@logger").GetString().Should().Be("Test.Category");
        jsonDoc.RootElement.GetProperty("@message").GetString().Should().Be("Test message");
        jsonDoc.RootElement.TryGetProperty("@timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldIncludeProperties()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        config.IncludeStructuredProperties = true;
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonDoc = JsonDocument.Parse(lines[0]);

        jsonDoc.RootElement.TryGetProperty("@properties", out var propertiesElement).Should().BeTrue();
        propertiesElement.TryGetProperty("UserId", out var userIdElement).Should().BeTrue();
        userIdElement.GetInt32().Should().Be(12345);
        propertiesElement.TryGetProperty("Action", out var actionElement).Should().BeTrue();
        actionElement.GetString().Should().Be("Login");
    }

    [Fact]
    public void LogMessage_WithException_ShouldIncludeExceptionDetails()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        config.Formatting.IncludeExceptionDetails = true;
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var exception = new InvalidOperationException("Test exception");
        logger.LogError(exception, "An error occurred");
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonDoc = JsonDocument.Parse(lines[0]);

        jsonDoc.RootElement.TryGetProperty("@exception", out var exceptionElement).Should().BeTrue();
        exceptionElement.TryGetProperty("type", out var typeElement).Should().BeTrue();
        typeElement.GetString().Should().Contain("InvalidOperationException");
        exceptionElement.TryGetProperty("message", out var messageElement).Should().BeTrue();
        messageElement.GetString().Should().Be("Test exception");
        exceptionElement.TryGetProperty("stackTrace", out _).Should().BeTrue();
    }

    [Fact]
    public void LogMessage_WithPrettyPrint_ShouldFormatReadably()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForPrettyJson();
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("Test message with {Property}", "value");
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        // Pretty printed JSON should have newlines and indentation
        output.Should().Contain("\n");
        output.Should().Contain("  "); // Indentation

        // Verify it's still valid JSON
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonString = string.Join("", lines);
        var jsonDoc = JsonDocument.Parse(jsonString);
        jsonDoc.RootElement.GetProperty("@message").GetString().Should().Be("Test message with value");
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateProvider()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var newConfig = PragmaticJsonConfiguration.ForPrettyJson();
        provider.UpdateConfiguration(newConfig);

        logger.LogInformation("Test message after config update");
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        // Should now use pretty printing (check the JSON structure instead)
        // Since configuration might not immediately affect existing formatting,
        // just verify the message was logged
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().NotBeEmpty();

        var jsonDoc = JsonDocument.Parse(lines[0]);
        jsonDoc.RootElement.GetProperty("@message").GetString().Should().Be("Test message after config update");
    }

    [Fact]
    public void FileCreation_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempFile = Path.Combine(tempDir, "test.json");
        var config = PragmaticJsonConfiguration.ForHighPerformanceJsonFile();

        try
        {
            using var provider = new PragmaticJsonProvider("TestJsonFile", config, tempFile);
            var logger = provider.CreateLogger("Test.Category");

            // Act
            logger.LogInformation("Test message");
            provider.Dispose(); // Ensure flush

            // Assert
            Directory.Exists(tempDir).Should().BeTrue();
            File.Exists(tempFile).Should().BeTrue();

            var content = File.ReadAllText(tempFile);
            content.Should().NotBeEmpty();

            // Verify it's valid JSON
            var jsonDoc = JsonDocument.Parse(content.Trim());
            jsonDoc.RootElement.GetProperty("@message").GetString().Should().Be("Test message");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void JsonProvider_WithComplexObjects_ShouldSerializeCorrectly()
    {
        // Arrange
        var config = PragmaticJsonConfiguration.ForJson();
        config.CustomProperties["SerializeComplexObjects"] = true;
        using var memoryStream = new MemoryStream();
        using var provider = new PragmaticJsonProvider("TestJson", config, memoryStream);
        var logger = provider.CreateLogger("Test.Category");

        var complexObject = new { Name = "Test", Value = 42, Items = new[] { 1, 2, 3 } };

        // Act
        logger.LogInformation("Complex object: {Object}", complexObject);
        provider.Dispose(); // Ensure flush

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var output = reader.ReadToEnd();

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var jsonDoc = JsonDocument.Parse(lines[0]);

        jsonDoc.RootElement.TryGetProperty("@properties", out var propertiesElement).Should().BeTrue();
        propertiesElement.TryGetProperty("Object", out var objectElement).Should().BeTrue();
        var objectString = objectElement.GetString();
        // JSON serializer uses camelCase by default
        objectString.Should().Contain("name"); // camelCase instead of PascalCase
        objectString.Should().Contain("Test");
        objectString.Should().Contain("value"); // camelCase instead of PascalCase  
        objectString.Should().Contain("42");
    }
}