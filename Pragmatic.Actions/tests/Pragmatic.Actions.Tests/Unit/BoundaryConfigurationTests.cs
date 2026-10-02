using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Boundary;
using Pragmatic.Actions.EFCore;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for BoundaryConfiguration fluent API.
///     Covers: UseLocal, UseRemote, UseDatabase, Validate, fluent chaining, edge cases.
/// </summary>
public class BoundaryConfigurationTests
{
    // =========================================================================
    // Test boundary markers
    // =========================================================================

    private sealed class StudentsBoundary : IBoundary;
    private sealed class OrdersBoundary : IBoundary;

    // =========================================================================
    // Tests — UseLocal
    // =========================================================================

    [Fact]
    public void UseLocal_SetsModeToLocal()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        config.UseLocal();

        config.Mode.Should().Be(BoundaryMode.Local);
        config.RemoteBaseUrl.Should().BeNull();
    }

    [Fact]
    public void UseLocal_ReturnsSameInstanceForFluent()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        var result = config.UseLocal();

        result.Should().BeSameAs(config);
    }

    // =========================================================================
    // Tests — UseRemote
    // =========================================================================

    [Fact]
    public void UseRemote_SetsModeAndUrl()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        config.UseRemote("https://api.example.com");

        config.Mode.Should().Be(BoundaryMode.Remote);
        config.RemoteBaseUrl.Should().Be("https://api.example.com");
    }

    [Fact]
    public void UseRemote_TrimsTrailingSlash()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        config.UseRemote("https://api.example.com/");

        config.RemoteBaseUrl.Should().Be("https://api.example.com");
    }

    [Fact]
    public void UseRemote_NullOrEmptyUrl_ThrowsArgumentException()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        var actNull = () => config.UseRemote(null!);
        var actEmpty = () => config.UseRemote("");
        var actWhitespace = () => config.UseRemote("   ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UseRemote_ClearsDbContextOptions()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseLocal().UseDatabase(opt => opt.UseInMemoryDatabase("test"));

        config.UseRemote("https://api.example.com");

        config.DatabaseOptions.Should().BeNull();
    }

    // =========================================================================
    // Tests — UseDatabase
    // =========================================================================

    [Fact]
    public void UseDatabase_OnLocal_SetsDbContextOptions()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        Action<DbContextOptionsBuilder> dbConfig = opt => opt.UseInMemoryDatabase("test");

        config.UseLocal().UseDatabase(dbConfig);

        config.DatabaseOptions.Should().NotBeNull();
    }

    [Fact]
    public void UseDatabase_OnRemote_ThrowsInvalidOperationException()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseRemote("https://api.example.com");

        var act = () => config.UseDatabase(opt => opt.UseInMemoryDatabase("test"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*remote*");
    }

    [Fact]
    public void UseDatabase_NullConfigure_ThrowsArgumentNullException()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseLocal();

        var act = () => config.UseDatabase(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // =========================================================================
    // Tests — Validate
    // =========================================================================

    [Fact]
    public void Validate_LocalWithoutDatabase_ThrowsInvalidOperationException()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseLocal();

        var act = () => config.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*database configuration*");
    }

    [Fact]
    public void Validate_LocalWithDatabase_Succeeds()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseLocal().UseDatabase(opt => opt.UseInMemoryDatabase("test"));

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_RemoteWithUrl_Succeeds()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseRemote("https://api.example.com");

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    // =========================================================================
    // Tests — BoundaryType
    // =========================================================================

    [Fact]
    public void BoundaryType_ReturnsCorrectType()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        config.BoundaryType.Should().Be(typeof(StudentsBoundary));
    }

    // =========================================================================
    // Tests — Default state
    // =========================================================================

    [Fact]
    public void DefaultState_ModeIsLocal()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();

        config.Mode.Should().Be(BoundaryMode.Local);
        config.RemoteBaseUrl.Should().BeNull();
        config.DatabaseOptions.Should().BeNull();
    }

    // =========================================================================
    // Tests — Switching modes
    // =========================================================================

    [Fact]
    public void SwitchFromRemoteToLocal_ClearsRemoteUrl()
    {
        var config = new BoundaryConfiguration<StudentsBoundary>();
        config.UseRemote("https://api.example.com");

        config.UseLocal();

        config.Mode.Should().Be(BoundaryMode.Local);
        config.RemoteBaseUrl.Should().BeNull();
    }
}
