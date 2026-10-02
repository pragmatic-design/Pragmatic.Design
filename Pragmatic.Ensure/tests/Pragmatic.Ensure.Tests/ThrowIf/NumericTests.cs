using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class NumericTests
{
    #region ThrowIfNegative (INumber<T>)

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void ThrowIfNegative_WithNegativeInt_ThrowsArgumentOutOfRangeException(int value)
    {
        var act = () => Ensure.ThrowIfNegative(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void ThrowIfNegative_WithNonNegativeInt_DoesNotThrow(int value)
    {
        var act = () => Ensure.ThrowIfNegative(value);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNegative_WithNegativeDouble_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfNegative(-0.001);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfNegative_WithNegativeDecimal_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfNegative(-0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ThrowIfNegativeOrZero (INumber<T>)

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ThrowIfNegativeOrZero_WithNonPositive_ThrowsArgumentOutOfRangeException(int value)
    {
        var act = () => Ensure.ThrowIfNegativeOrZero(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void ThrowIfNegativeOrZero_WithPositive_DoesNotThrow(int value)
    {
        var act = () => Ensure.ThrowIfNegativeOrZero(value);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfZero (INumber<T>)

    [Fact]
    public void ThrowIfZero_WithZeroInt_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfZero(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfZero_WithZeroDouble_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfZero(0.0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ThrowIfZero_WithNonZero_DoesNotThrow(int value)
    {
        var act = () => Ensure.ThrowIfZero(value);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfPositiveOrZero (INumber<T>) — includes zero, and the name says so

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void ThrowIfPositive_WithPositive_ThrowsArgumentOutOfRangeException(int value)
    {
        var act = () => Ensure.ThrowIfPositiveOrZero(value);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ThrowIfPositive_WithNegative_DoesNotThrow(int value)
    {
        var act = () => Ensure.ThrowIfPositiveOrZero(value);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfPositive_WithZero_Throws()
    {
        // Aligned with INumber<T>.IsPositive semantics — zero is considered
        // positive (sign bit clear). Use ThrowIfPositiveOrZero for the same
        // behaviour with a self-documenting name.
        var act = () => Ensure.ThrowIfPositiveOrZero(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ThrowIfOutOfRange (IComparable<T>)

    [Theory]
    [InlineData(0, 1, 10)] // Below min
    [InlineData(11, 1, 10)] // Above max
    public void ThrowIfOutOfRange_WithOutOfRange_ThrowsArgumentOutOfRangeException(int value, int min, int max)
    {
        var act = () => Ensure.ThrowIfOutOfRange(value, min, max);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1, 1, 10)] // Min boundary
    [InlineData(5, 1, 10)] // Middle
    [InlineData(10, 1, 10)] // Max boundary
    public void ThrowIfOutOfRange_WithInRange_DoesNotThrow(int value, int min, int max)
    {
        var act = () => Ensure.ThrowIfOutOfRange(value, min, max);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfOutOfRange_WithDateTimeInRange_DoesNotThrow()
    {
        var min = new DateTime(2020, 1, 1);
        var max = new DateTime(2020, 12, 31);
        var value = new DateTime(2020, 6, 15);

        var act = () => Ensure.ThrowIfOutOfRange(value, min, max);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfGreaterThan (IComparable<T>)

    [Fact]
    public void ThrowIfGreaterThan_WithGreater_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfGreaterThan(11, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(10, 10)] // Equal
    [InlineData(9, 10)] // Less
    public void ThrowIfGreaterThan_WithNotGreater_DoesNotThrow(int value, int max)
    {
        var act = () => Ensure.ThrowIfGreaterThan(value, max);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfLessThan (IComparable<T>)

    [Fact]
    public void ThrowIfLessThan_WithLess_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfLessThan(5, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(10, 10)] // Equal
    [InlineData(11, 10)] // Greater
    public void ThrowIfLessThan_WithNotLess_DoesNotThrow(int value, int min)
    {
        var act = () => Ensure.ThrowIfLessThan(value, min);

        act.Should().NotThrow();
    }

    #endregion

    #region Aliases (Deprecated)

    [Fact]
    public void ThrowIfBelowMin_IsAliasForThrowIfLessThan()
    {
        var act = () => Ensure.ThrowIfLessThan(5, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfAboveMax_IsAliasForThrowIfGreaterThan()
    {
        var act = () => Ensure.ThrowIfGreaterThan(15, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion
}