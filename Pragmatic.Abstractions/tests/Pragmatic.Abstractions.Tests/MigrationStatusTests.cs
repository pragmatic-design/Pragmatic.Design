using Pragmatic.Testing.Assertions;
using Pragmatic.ControlPlane;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class MigrationStatusTests
{
    private static MigrationStatus Create(bool isError, string? errorMessage, double percent = 50)
        => new("app-db", TotalChanges: 10, AppliedChanges: 5, percent, isError, errorMessage);

    [Fact]
    public void Ctor_ErrorWithMessage_IsConsistent()
    {
        var status = Create(isError: true, errorMessage: "boom");

        status.IsError.Should().BeTrue();
        status.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public void Ctor_SuccessWithoutMessage_IsConsistent()
        => Create(isError: false, errorMessage: null).IsError.Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Ctor_ErrorWithoutMessage_Throws(string? message)
    {
        var act = () => Create(isError: true, errorMessage: message);
        act.Should().Throw<ArgumentException>().WithParameterName("ErrorMessage");
    }

    [Fact]
    public void Ctor_SuccessWithMessage_Throws()
    {
        var act = () => Create(isError: false, errorMessage: "leftover");
        act.Should().Throw<ArgumentException>().WithParameterName("ErrorMessage");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(100.1)]
    public void Ctor_PercentOutOfRange_Throws(double percent)
    {
        var act = () => Create(isError: false, errorMessage: null, percent);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Ctor_PercentBoundaries_AreInclusive(double percent)
        => Create(isError: false, errorMessage: null, percent).ProgressPercent.Should().Be(percent);

    // =========================================================================
    // The invariants must hold through `init`, not only the constructor: a
    // property initializer runs only at construction, and an object
    // initializer or a `with` assigns through `init` and would bypass it.
    // =========================================================================

    [Theory]
    [InlineData(-0.1)]
    [InlineData(500)]
    public void ObjectInitializer_PercentOutOfRange_Throws(double percent)
    {
        var act = () => new MigrationStatus("app-db", 10, 5, 50, false, null) { ProgressPercent = percent };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Theory]
    [InlineData(-7)]
    [InlineData(101)]
    public void With_PercentOutOfRange_Throws(double percent)
    {
        var status = Create(isError: false, errorMessage: null);

        var act = () => status with { ProgressPercent = percent };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Fact]
    public void Ctor_PercentNaN_Throws()
    {
        var act = () => Create(isError: false, errorMessage: null, double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ProgressPercent");
    }

    [Fact]
    public void With_ErrorMessageSet_TurnsIsErrorOn()
    {
        // IsError is derived from ErrorMessage, so the pair cannot drift:
        // there is no way to reach a status that claims success and carries an error.
        var status = Create(isError: false, errorMessage: null);

        var errored = status with { ErrorMessage = "boom" };

        errored.IsError.Should().BeTrue();
        errored.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public void With_ErrorMessageCleared_TurnsIsErrorOff()
    {
        var status = Create(isError: true, errorMessage: "boom");

        var recovered = status with { ErrorMessage = null };

        recovered.IsError.Should().BeFalse();
    }
}
