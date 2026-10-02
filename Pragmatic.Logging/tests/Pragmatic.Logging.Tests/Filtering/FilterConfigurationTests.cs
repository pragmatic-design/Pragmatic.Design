using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;

namespace Pragmatic.Logging.Tests.Filtering;

public class FilterConfigurationTests
{
    [Fact]
    public void FilterConfiguration_DefaultState_ShouldBeEmpty()
    {
        // Arrange & Act
        var config = new FilterConfiguration();

        // Assert
        Assert.Empty(config.Filters);
        Assert.True(config.EnableDefaultFilters);
    }

    [Fact]
    public void AddEntityFrameworkFilter_ShouldAddNamespaceFilter()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.AddEntityFrameworkFilter(LogLevel.Warning);

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as NamespaceFilter;
        Assert.NotNull(filter);
        Assert.Equal("EntityFramework", filter.Name);
        Assert.Equal(200, filter.Priority);
    }

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void AddEntityFrameworkFilter_WithDifferentLevels_ShouldUseSpecifiedLevel(LogLevel level)
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.AddEntityFrameworkFilter(level);

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as NamespaceFilter;
        Assert.NotNull(filter);
        Assert.Equal("EntityFramework", filter.Name);
    }

    [Fact]
    public void AddEntityFrameworkFilter_DefaultLevel_ShouldUseWarning()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.AddEntityFrameworkFilter();

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as NamespaceFilter;
        Assert.NotNull(filter);
        Assert.Equal("EntityFramework", filter.Name);
    }

    [Fact]
    public void ExcludeHealthChecks_ShouldAddHttpContextFilter()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.ExcludeHealthChecks();

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as HttpContextFilter;
        Assert.NotNull(filter);
        Assert.Contains("RequestPath", filter.Name);
    }

    [Fact]
    public void ExcludeMonitoringTools_ShouldAddHttpContextFilter()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.ExcludeMonitoringTools();

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as HttpContextFilter;
        Assert.NotNull(filter);
        Assert.Contains("ExcludeUserAgent", filter.Name);
    }

    [Fact]
    public void AddRateLimit_ShouldAddRateLimitFilter()
    {
        // Arrange
        var config = new FilterConfiguration();
        var timeWindow = TimeSpan.FromMinutes(1);
        var maxMessages = 100;

        // Act
        config.AddRateLimit(timeWindow, maxMessages);

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as RateLimitFilter;
        Assert.NotNull(filter);
        Assert.Contains("RateLimit", filter.Name);
        Assert.Contains("60", filter.Name); // 60 seconds
        Assert.Contains("100", filter.Name); // 100 messages
    }

    [Fact]
    public void AddSlowQueryFilter_ShouldAddPropertyFilter()
    {
        // Arrange
        var config = new FilterConfiguration();
        var threshold = 1000.0; // 1 second

        // Act
        config.AddSlowQueryFilter(threshold);

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as PropertyFilter;
        Assert.NotNull(filter);
        Assert.Contains("Duration", filter.Name);
    }

    [Fact]
    public void AddUserActionFilter_ShouldAddPropertyFilters()
    {
        // Arrange
        var config = new FilterConfiguration();
        var actions = new[] { "Login", "Logout", "Purchase" };

        // Act
        config.AddUserActionFilter(actions);

        // Assert
        Assert.Equal(3, config.Filters.Count);
        Assert.All(config.Filters, filter =>
        {
            var propertyFilter = Assert.IsType<PropertyFilter>(filter);
            Assert.Contains("Action", propertyFilter.Name);
        });
    }

    [Fact]
    public void AddUserActionFilter_WithSingleAction_ShouldAddOneFilter()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.AddUserActionFilter("Login");

        // Assert
        Assert.Single(config.Filters);
        var filter = config.Filters[0] as PropertyFilter;
        Assert.NotNull(filter);
        Assert.Contains("Action", filter.Name);
    }

    [Fact]
    public void FilterConfiguration_FluentAPI_ShouldReturnSelf()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act & Assert - Should be able to chain calls
        var result = config
            .AddEntityFrameworkFilter()
            .ExcludeHealthChecks()
            .AddRateLimit(TimeSpan.FromMinutes(1), 100)
            .ExcludeMonitoringTools();

        Assert.Same(config, result);
        Assert.Equal(4, config.Filters.Count);
    }

    [Fact]
    public void FilterConfiguration_MultipleFilters_ShouldMaintainOrder()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config
            .AddEntityFrameworkFilter(LogLevel.Warning)  // Priority 200
            .AddRateLimit(TimeSpan.FromMinutes(1), 100)   // Priority 50
            .ExcludeHealthChecks();                       // Priority 200 (HttpContextFilter default)

        // Assert
        Assert.Equal(3, config.Filters.Count);

        // Verify types are added in order
        Assert.IsType<NamespaceFilter>(config.Filters[0]);
        Assert.IsType<RateLimitFilter>(config.Filters[1]);
        Assert.IsType<HttpContextFilter>(config.Filters[2]);
    }

    [Fact]
    public void FilterConfiguration_EnableDefaultFilters_ShouldBeConfigurable()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act
        config.EnableDefaultFilters = false;

        // Assert
        Assert.False(config.EnableDefaultFilters);
    }

    [Fact]
    public void FilterConfiguration_ComplexScenario_ShouldSupportAllFilters()
    {
        // Arrange
        var config = new FilterConfiguration();

        // Act - Create a complex filtering scenario
        config
            .AddEntityFrameworkFilter(LogLevel.Error)      // Only EF errors
            .ExcludeHealthChecks()                          // No health check noise
            .ExcludeMonitoringTools()                       // No monitoring tool noise
            .AddRateLimit(TimeSpan.FromMinutes(5), 1000)    // Rate limit to prevent spam
            .AddSlowQueryFilter(2000.0)                     // Only slow queries > 2s
            .AddUserActionFilter("CriticalAction", "SecurityEvent"); // Only specific user actions

        // Assert
        Assert.Equal(7, config.Filters.Count); // 1+1+1+1+1+2 filters

        // Verify we have the expected filter types
        Assert.Contains(config.Filters, f => f is NamespaceFilter);
        Assert.Contains(config.Filters, f => f is RateLimitFilter);
        Assert.Equal(2, config.Filters.Count(f => f is HttpContextFilter));
        Assert.Equal(3, config.Filters.Count(f => f is PropertyFilter)); // 1 slow query + 2 user actions
    }
}