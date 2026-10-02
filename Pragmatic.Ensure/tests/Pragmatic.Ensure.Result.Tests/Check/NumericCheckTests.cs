using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Result.Tests.Check;

public class NumericCheckTests
{
    private static readonly TestError Error = new("NUMERIC_ERROR");

    #region Positive

    [Fact]
    public void Positive_WithNegative_ReturnsFailure()
    {
        var result = Result.Check.Positive(-5, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Positive_WithZero_ReturnsFailure()
    {
        var result = Result.Check.Positive(0, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Positive_WithPositive_ReturnsSuccess()
    {
        var result = Result.Check.Positive(5, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Positive - Lazy Factory

    [Fact]
    public void Positive_WithFactory_WithNegative_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.Positive(-5, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void Positive_WithFactory_WithPositive_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.Positive(5, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotNegative

    [Fact]
    public void NotNegative_WithNegative_ReturnsFailure()
    {
        var result = Result.Check.NotNegative(-5, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNegative_WithZero_ReturnsSuccess()
    {
        var result = Result.Check.NotNegative(0, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotNegative_WithPositive_ReturnsSuccess()
    {
        var result = Result.Check.NotNegative(5, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNegative - Lazy Factory

    [Fact]
    public void NotNegative_WithFactory_WithNegative_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotNegative(-5, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNegative_WithFactory_WithZero_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotNegative(0, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotZero

    [Fact]
    public void NotZero_WithZero_ReturnsFailure()
    {
        var result = Result.Check.NotZero(0, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotZero_WithNonZero_ReturnsSuccess()
    {
        var result = Result.Check.NotZero(42, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotZero_WithNegative_ReturnsSuccess()
    {
        var result = Result.Check.NotZero(-1, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotZero - Lazy Factory

    [Fact]
    public void NotZero_WithFactory_WithZero_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotZero(0, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotZero_WithFactory_WithNonZero_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotZero(42, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region InRange

    [Fact]
    public void InRange_WithBelowMin_ReturnsFailure()
    {
        var result = Result.Check.InRange(5, 10, 100, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InRange_WithAboveMax_ReturnsFailure()
    {
        var result = Result.Check.InRange(150, 10, 100, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InRange_WithAtMin_ReturnsSuccess()
    {
        var result = Result.Check.InRange(10, 10, 100, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InRange_WithAtMax_ReturnsSuccess()
    {
        var result = Result.Check.InRange(100, 10, 100, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InRange_WithInRange_ReturnsSuccess()
    {
        var result = Result.Check.InRange(50, 10, 100, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region InRange - Lazy Factory

    [Fact]
    public void InRange_WithFactory_WithBelowMin_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.InRange(5, 10, 100, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void InRange_WithFactory_WithInRange_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.InRange(50, 10, 100, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region AtLeast

    [Fact]
    public void AtLeast_WithBelow_ReturnsFailure()
    {
        var result = Result.Check.AtLeast(5, 10, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AtLeast_WithEqual_ReturnsSuccess()
    {
        var result = Result.Check.AtLeast(10, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AtLeast_WithAbove_ReturnsSuccess()
    {
        var result = Result.Check.AtLeast(15, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region AtLeast - Lazy Factory

    [Fact]
    public void AtLeast_WithFactory_WithBelow_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.AtLeast(5, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void AtLeast_WithFactory_WithAbove_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.AtLeast(15, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region AtMost

    [Fact]
    public void AtMost_WithAbove_ReturnsFailure()
    {
        var result = Result.Check.AtMost(15, 10, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void AtMost_WithEqual_ReturnsSuccess()
    {
        var result = Result.Check.AtMost(10, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AtMost_WithBelow_ReturnsSuccess()
    {
        var result = Result.Check.AtMost(5, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region AtMost - Lazy Factory

    [Fact]
    public void AtMost_WithFactory_WithAbove_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.AtMost(15, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void AtMost_WithFactory_WithBelow_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.AtMost(5, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion
}