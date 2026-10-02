using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class OtherTests
{
    #region ThrowIfEmpty (Guid)

    [Fact]
    public void ThrowIfEmpty_Guid_WithEmpty_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfEmpty(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_Guid_WithValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfEmpty(Guid.NewGuid());

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfDefault (DateTime)

    [Fact]
    public void ThrowIfDefault_DateTime_WithDefault_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfDefault(default);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfDefault_DateTime_WithValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfDefault(DateTime.UtcNow);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfInPast (DateTime)

    [Fact]
    public void ThrowIfInPast_DateTime_WithPast_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfInPast(DateTime.UtcNow.AddDays(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfInPast_DateTime_WithFuture_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfInPast(DateTime.UtcNow.AddDays(1));

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfInFuture (DateTime)

    [Fact]
    public void ThrowIfInFuture_DateTime_WithFuture_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfInFuture(DateTime.UtcNow.AddDays(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfInFuture_DateTime_WithPast_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfInFuture(DateTime.UtcNow.AddDays(-1));

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfDefault (DateTimeOffset)

    [Fact]
    public void ThrowIfDefault_DateTimeOffset_WithDefault_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfDefault(default(DateTimeOffset));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfDefault_DateTimeOffset_WithValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfDefault(DateTimeOffset.UtcNow);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfInPast (DateTimeOffset)

    [Fact]
    public void ThrowIfInPast_DateTimeOffset_WithPast_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfInPast(DateTimeOffset.UtcNow.AddDays(-1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfInPast_DateTimeOffset_WithFuture_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfInPast(DateTimeOffset.UtcNow.AddDays(1));

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfInFuture (DateTimeOffset)

    [Fact]
    public void ThrowIfInFuture_DateTimeOffset_WithFuture_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfInFuture(DateTimeOffset.UtcNow.AddDays(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfInFuture_DateTimeOffset_WithPast_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfInFuture(DateTimeOffset.UtcNow.AddDays(-1));

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfTrue

    [Fact]
    public void ThrowIfTrue_WithTrue_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfTrue(true);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfTrue_WithFalse_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfTrue(false);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfTrue_WithCustomMessage_UsesMessage()
    {
        var act = () => Ensure.ThrowIfTrue(true, "Custom error message");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Custom error message*");
    }

    #endregion

    #region ThrowIfFalse

    [Fact]
    public void ThrowIfFalse_WithFalse_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfFalse(false);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfFalse_WithTrue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfFalse(true);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfFalse_WithCustomMessage_UsesMessage()
    {
        var act = () => Ensure.ThrowIfFalse(false, "Custom error message");

        act.Should().Throw<ArgumentException>()
            .WithMessage("Custom error message*");
    }

    #endregion

    #region ThrowIfEqual

    [Fact]
    public void ThrowIfEqual_WithEqualValues_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfEqual(42, 42);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must not equal*42*");
    }

    [Fact]
    public void ThrowIfEqual_WithDifferentValues_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfEqual(42, 43);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfEqual_WithStrings_ThrowsWhenEqual()
    {
        var act = () => Ensure.ThrowIfEqual("abc", "abc");

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ThrowIfNotEqual

    [Fact]
    public void ThrowIfNotEqual_WithDifferentValues_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfNotEqual(42, 43);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must equal*43*Actual*42*");
    }

    [Fact]
    public void ThrowIfNotEqual_WithEqualValues_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotEqual(42, 42);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfDefault (Generic Struct)

    [Fact]
    public void ThrowIfDefault_Int_WithDefault_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfDefault(0);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must not be default*");
    }

    [Fact]
    public void ThrowIfDefault_Int_WithValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfDefault(42);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfDefault_Guid_WithEmpty_ThrowsArgumentException()
    {
        // Generic ThrowIfDefault<T> where T : struct covers Guid too
        var act = () => Ensure.ThrowIfDefault(Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfDefault_Guid_WithValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfDefault(Guid.NewGuid());

        act.Should().NotThrow();
    }

    #endregion
}