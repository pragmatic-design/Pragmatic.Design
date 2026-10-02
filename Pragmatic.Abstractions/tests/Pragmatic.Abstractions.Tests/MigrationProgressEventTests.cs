using Pragmatic.Testing.Assertions;
using Pragmatic.Maintenance;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class MigrationProgressEventTests
{
    [Fact]
    public void Failed_SetsErrorStateAndCarriesMessage()
    {
        var evt = MigrationProgressEvent.Failed("boom");

        evt.IsError.Should().BeTrue();
        evt.Message.Should().Be("boom");
        evt.Phase.Should().Be("error");
    }

    [Fact]
    public void Failed_WithException_PopulatesErrorDetail()
    {
        var ex = new InvalidOperationException("kaput");

        var evt = MigrationProgressEvent.Failed("summary", ex);

        evt.ErrorDetail.Should().Contain("kaput");
    }

    [Fact]
    public void Failed_WithCustomPhaseAndDatabase_AreCarried()
    {
        var evt = MigrationProgressEvent.Failed("m", phase: "seeding", databaseName: "tenant_db");

        evt.Phase.Should().Be("seeding");
        evt.DatabaseName.Should().Be("tenant_db");
    }

    [Fact]
    public void NonFailedEvent_IsNotError()
    {
        var evt = new MigrationProgressEvent("migration", "applying", ProgressPercent: 50);

        evt.IsError.Should().BeFalse();
        evt.ErrorDetail.Should().BeNull();
        evt.ProgressPercent.Should().Be(50);
    }

    // =========================================================================
    // The range invariant must hold on every path that can set the property, not
    // only on the constructor. A validating property *initializer* runs once at
    // construction; an object initializer and `with` assign through the `init`
    // accessor afterwards, so a check in the initializer alone would skip them.
    // =========================================================================

    [Theory]
    [InlineData(-0.1)]
    [InlineData(500)]
    public void ObjectInitializer_PercentOutOfRange_Throws(double percent)
    {
        var act = () => new MigrationProgressEvent("migration", "applying") { ProgressPercent = percent };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(101)]
    public void With_PercentOutOfRange_Throws(double percent)
    {
        var evt = new MigrationProgressEvent("migration", "applying", ProgressPercent: 50);

        var act = () => evt with { ProgressPercent = percent };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Fact]
    public void Ctor_PercentNaN_Throws()
    {
        // NaN compares false against every relational operator, so a `< 0 or > 100`
        // pattern lets it through — the range check has to reject it explicitly.
        var act = () => new MigrationProgressEvent("migration", "applying", ProgressPercent: double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Fact]
    public void With_PercentNull_IsAccepted()
    {
        // null means "indeterminate step" and stays legal on every path.
        var evt = new MigrationProgressEvent("migration", "applying", ProgressPercent: 50);

        (evt with { ProgressPercent = null }).ProgressPercent.Should().BeNull();
    }

    [Fact]
    public void With_TimestampNull_StaysNonNull()
    {
        // The XML doc promises "always non-null at runtime"; `with { Timestamp = null }`
        // would falsify it if the default were resolved in the initializer only.
        var evt = new MigrationProgressEvent("migration", "applying");

        (evt with { Timestamp = null }).Timestamp.Should().NotBeNull();
    }
}
