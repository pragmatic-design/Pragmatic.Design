using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Caching.Diagnostics;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests for <see cref="CachingDiagnostics"/> static instruments and source configuration.
/// </summary>
public class CachingDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticCaching()
    {
        CachingDiagnostics.SourceName.Should().Be("Pragmatic.Caching");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        CachingDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Caching");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        CachingDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        CachingDiagnostics.Meter.Name.Should().Be("Pragmatic.Caching");
    }

    [Fact]
    public void CacheHits_IsNotNull()
    {
        CachingDiagnostics.CacheHits.Should().NotBeNull();
    }

    [Fact]
    public void CacheMisses_IsNotNull()
    {
        CachingDiagnostics.CacheMisses.Should().NotBeNull();
    }

    [Fact]
    public void CacheSets_IsNotNull()
    {
        CachingDiagnostics.CacheSets.Should().NotBeNull();
    }

    [Fact]
    public void CacheInvalidations_IsNotNull()
    {
        CachingDiagnostics.CacheInvalidations.Should().NotBeNull();
    }
}
