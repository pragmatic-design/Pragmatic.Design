using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Tests.Providers;

public class PragmaticFileProviderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly List<string> _filesToCleanup = new();

    public PragmaticFileProviderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"PragmaticLogging_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        // Clean up test files
        foreach (var file in _filesToCleanup)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception)
            {
                // Ignore cleanup errors
            }
        }

        try
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, true);
        }
        catch (Exception)
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();

        // Act
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);

        // Assert
        provider.Name.Should().Be("TestFile");
        provider.Configuration.Should().Be(config);
    }

    [Fact]
    public void Constructor_WithBatchingEnabled_ShouldNotThrowException()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.Performance.EnableBatching = true; // File provider supports batching

        // Act & Assert
        var act = () => new PragmaticFileProvider("TestFile", config, filePath);
        act.Should().NotThrow();
    }

    [Fact]
    public void ForFile_ShouldCreateOptimalConfiguration()
    {
        // Act
        var config = PragmaticProviderConfiguration.ForFile();

        // Assert
        config.MinimumLevel.Should().Be(LogLevel.Debug);
        config.IncludeStructuredProperties.Should().BeTrue();
        config.IncludeContextEnrichment.Should().BeTrue();

        // File should use batching for performance
        config.Performance.EnableBatching.Should().BeTrue();
        config.Performance.UseZeroAllocation.Should().BeTrue();

        // Should include all context by default
        config.ContextFilter.Mode.Should().Be(ContextFilterMode.Include);

        // File-optimized formatting
        config.Formatting.TimestampFormat.Should().Be("yyyy-MM-dd HH:mm:ss.fff");
        config.Formatting.UseUtcTimestamp.Should().BeTrue(); // UTC for file logs
        config.Formatting.MaxMessageLength.Should().Be(32768); // Allow longer messages
        config.Formatting.MessageTemplate.Should().Contain("{Timestamp}");
        config.Formatting.MessageTemplate.Should().Contain("{Level}");
        config.Formatting.MessageTemplate.Should().Contain("{Category}");
        config.Formatting.MessageTemplate.Should().Contain("{Message}");

        // File-specific custom properties
        config.CustomProperties["MaxFileSize"].Should().Be(10 * 1024 * 1024L); // 10MB
        config.CustomProperties["RollingInterval"].Should().Be("Day");
        config.CustomProperties["MaxRetainedFiles"].Should().Be(31);
        config.CustomProperties["FlushAfterWrite"].Should().Be(true);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLogger()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);

        // Act
        var logger = provider.CreateLogger("Test.Category");

        // Assert
        logger.Should().NotBeNull();
    }

    [Fact]
    public void IsEnabled_WithConfiguredLevel_ShouldReturnCorrectResult()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.MinimumLevel = LogLevel.Warning;
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
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
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.MinimumLevel = LogLevel.Warning;
        config.CategoryLevels["Test.Category"] = LogLevel.Debug;
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");
        var otherLogger = provider.CreateLogger("Other.Category");

        // Act & Assert
        logger.IsEnabled(LogLevel.Debug).Should().BeTrue();
        logger.IsEnabled(LogLevel.Information).Should().BeTrue();

        otherLogger.IsEnabled(LogLevel.Debug).Should().BeFalse();
        otherLogger.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void GetMetrics_ShouldReturnFileSpecificMetrics()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);

        // Act
        var metrics = provider.GetMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.CustomMetrics.Should().ContainKey("CurrentFilePath");
        metrics.CustomMetrics.Should().ContainKey("CurrentFileSize");
        metrics.CustomMetrics.Should().ContainKey("MaxFileSize");
        metrics.CustomMetrics.Should().ContainKey("RollingInterval");
        metrics.CustomMetrics.Should().ContainKey("MaxRetainedFiles");
        metrics.CustomMetrics.Should().ContainKey("IsFileOpen");
        metrics.CustomMetrics.Should().ContainKey("CanWrite");
    }

    [Fact]
    public void CheckHealth_ShouldReturnHealthyWhenFileAccessible()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);

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
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.MinimumLevel = LogLevel.Trace; // Enable all levels for testing
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.Log(logLevel, "Test message");

        // Dispose provider to ensure file is closed before reading
        provider.Dispose();

        // Assert
        File.Exists(filePath).Should().BeTrue();
        File.ReadAllText(filePath).Should().Contain(expectedLevel);
    }

    [Fact]
    public void LogMessage_WithStructuredProperties_ShouldIncludeProperties()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.IncludeStructuredProperties = true;
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        logger.LogInformation("User {UserId} performed {Action}", 12345, "Login");

        // Dispose provider to ensure file is closed before reading
        provider.Dispose();

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("UserId=12345");
        content.Should().Contain("Action=\"Login\"");
    }

    [Fact]
    public void LogMessage_WithException_ShouldIncludeExceptionDetails()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.Formatting.IncludeExceptionDetails = true;
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var exception = new InvalidOperationException("Test exception");
        logger.LogError(exception, "An error occurred");

        // Dispose provider to ensure file is closed before reading
        provider.Dispose();

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("Exception:");
        content.Should().Contain("InvalidOperationException");
        content.Should().Contain("Test exception");
    }

    [Fact]
    public void UpdateConfiguration_ShouldUpdateFormattingTemplate()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");

        // Act
        var newConfig = PragmaticProviderConfiguration.ForFile();
        newConfig.Formatting.MessageTemplate = "[{Level}] {Message}"; // Simplified template
        provider.UpdateConfiguration(newConfig);

        logger.LogInformation("Test message");

        // Dispose provider to ensure file is closed before reading
        provider.Dispose();

        // Assert
        File.Exists(filePath).Should().BeTrue();

        var content = File.ReadAllText(filePath);
        content.Should().Contain("[INFO] Test message");
        content.Should().NotContain("Test.Category"); // Category should not be in simplified template
    }

    [Fact]
    public void FileCreation_ShouldCreateDirectoryIfNotExists()
    {
        // Arrange
        var subDir = Path.Combine(_tempDirectory, "subdir");
        var filePath = Path.Combine(subDir, "test.log");
        _filesToCleanup.Add(filePath);
        var config = PragmaticProviderConfiguration.ForFile();

        // Act
        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");
        logger.LogInformation("Test message");

        // ⚠️ Disposed, not slept on. The provider writes through a channel drained by a background
        // task, and Dispose completes the channel, waits for that task and closes the writer — which
        // is the actual signal that the file is on disk. A fixed sleep is enough on an idle machine and
        // not enough when suites run in parallel. The tests beside this one do the same.
        provider.Dispose();

        // Assert
        Directory.Exists(subDir).Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void FileSizeRolling_ShouldRollFileWhenSizeExceeded()
    {
        // Arrange
        var filePath = GetTestFilePath("test.log");
        var config = PragmaticProviderConfiguration.ForFile();
        config.CustomProperties["MaxFileSize"] = 100L; // Very small file size for testing
        config.CustomProperties["RollingInterval"] = "None"; // Disable time-based rolling

        using var provider = new PragmaticFileProvider("TestFile", config, filePath);
        var logger = provider.CreateLogger("Test.Category");

        // Act - Write enough content to exceed file size
        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation("This is a test message to fill up the file {Index}", i);

            // Kept: this one spaces the writes so the size check between them has a chance to roll,
            // which is what the case is about. It is not what the assertion waits on — that is the
            // Dispose below.
            Thread.Sleep(10);
        }

        // Every queued write drained and the writer closed, so the directory listing below sees the
        // final state rather than whatever had reached disk within a fixed delay.
        provider.Dispose();

        // Assert
        // Check that files were created (original might have been rolled/archived)
        var directory = Path.GetDirectoryName(filePath);
        var files = Directory.GetFiles(directory!, Path.GetFileNameWithoutExtension(filePath) + "*" + Path.GetExtension(filePath));
        files.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FilePathTemplating_ShouldExpandDatePlaceholders()
    {
        // Arrange
        var templatePath = Path.Combine(_tempDirectory, "log-{Date}.log");
        var expectedPath = Path.Combine(_tempDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.log");
        _filesToCleanup.Add(expectedPath);
        var config = PragmaticProviderConfiguration.ForFile();

        // Act
        using var provider = new PragmaticFileProvider("TestFile", config, templatePath);
        var logger = provider.CreateLogger("Test.Category");
        logger.LogInformation("Test message");

        // Disposed rather than slept on, for the reason given in
        // FileCreation_ShouldCreateDirectoryIfNotExists.
        provider.Dispose();

        // Assert
        File.Exists(expectedPath).Should().BeTrue();
    }

    /// <summary>
    ///     Retention deletes the files the search pattern finds, so a pattern that keeps the template's
    ///     placeholders finds nothing and nothing is ever deleted: a host logging to
    ///     "showcase-{Date}.log" kept every file it ever rolled.
    /// </summary>
    [Theory]
    [InlineData("logs/showcase-{Date}.log", "showcase-2026-09-21.log")]
    [InlineData("logs/showcase-{Date}.log", "showcase-2026-09-21-20260921-145056.log")]
    [InlineData("logs/app-{Year}{Month}{Day}.log", "app-20260921-20260921-145056.log")]
    [InlineData("logs/app.log", "app-20260921-145056.log")]
    public void RetainedFileSearchPattern_FindsTheFilesTheTemplateProduces(string template, string fileName)
    {
        var pattern = PragmaticFileProvider.RetainedFileSearchPattern(template);

        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName).Should().BeTrue(
            $"'{pattern}' must find '{fileName}', or retention never deletes it");
    }

    /// <summary>The control: another provider's files in the same folder are not this one's to delete.</summary>
    [Fact]
    public void RetainedFileSearchPattern_DoesNotFindAnotherTemplatesFiles()
    {
        var pattern = PragmaticFileProvider.RetainedFileSearchPattern("logs/showcase-{Date}.log");

        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, "audit-2026-09-21.log").Should().BeFalse();
        System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, "showcase-2026-09-21.json").Should().BeFalse();
    }

    [Fact]
    public void ForHighPerformanceFile_ShouldCreatePerformanceOptimizedConfiguration()
    {
        // Act
        var config = PragmaticProviderConfiguration.ForHighPerformanceFile();

        // Assert
        config.Performance.EnableBatching.Should().BeTrue();
        config.CustomProperties["FlushAfterWrite"].Should().Be(false); // Less frequent flushing
        config.CustomProperties["MaxFileSize"].Should().Be(100 * 1024 * 1024L); // 100MB
        config.Formatting.MaxMessageLength.Should().Be(8192); // Shorter messages
    }

    private string GetTestFilePath(string fileName)
    {
        var filePath = Path.Combine(_tempDirectory, fileName);
        _filesToCleanup.Add(filePath);
        return filePath;
    }
}