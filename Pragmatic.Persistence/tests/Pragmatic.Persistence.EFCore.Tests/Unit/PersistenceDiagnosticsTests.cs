using System.Diagnostics;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     Tests for <see cref="PersistenceDiagnostics"/> static instruments and source configuration.
/// </summary>
public class PersistenceDiagnosticsTests
{
    [Fact]
    public void SourceName_IsPragmaticPersistence()
    {
        PersistenceDiagnostics.SourceName.Should().Be("Pragmatic.Persistence");
    }

    [Fact]
    public void ActivitySource_HasCorrectName()
    {
        PersistenceDiagnostics.ActivitySource.Name.Should().Be("Pragmatic.Persistence");
    }

    [Fact]
    public void ActivitySource_HasVersion()
    {
        PersistenceDiagnostics.ActivitySource.Version.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Meter_HasCorrectName()
    {
        PersistenceDiagnostics.Meter.Name.Should().Be("Pragmatic.Persistence");
    }

    [Fact]
    public void QueryDuration_IsNotNull()
    {
        PersistenceDiagnostics.QueryDuration.Should().NotBeNull();
    }

    [Fact]
    public void QueriesExecuted_IsNotNull()
    {
        PersistenceDiagnostics.QueriesExecuted.Should().NotBeNull();
    }

    [Fact]
    public void QueryFailures_IsNotNull()
    {
        PersistenceDiagnostics.QueryFailures.Should().NotBeNull();
    }

    [Fact]
    public void SaveChangesDuration_IsNotNull()
    {
        PersistenceDiagnostics.SaveChangesDuration.Should().NotBeNull();
    }

    [Fact]
    public void RowsAffected_IsNotNull()
    {
        PersistenceDiagnostics.RowsAffected.Should().NotBeNull();
    }

    [Fact]
    public void BulkInsertDuration_IsNotNull()
    {
        PersistenceDiagnostics.BulkInsertDuration.Should().NotBeNull();
    }

    [Fact]
    public void BulkUpsertDuration_IsNotNull()
    {
        PersistenceDiagnostics.BulkUpsertDuration.Should().NotBeNull();
    }

    [Fact]
    public void BulkOperations_IsNotNull()
    {
        PersistenceDiagnostics.BulkOperations.Should().NotBeNull();
    }

    [Fact]
    public void BulkFailures_IsNotNull()
    {
        PersistenceDiagnostics.BulkFailures.Should().NotBeNull();
    }
}
