using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Result.Tests.Check;

public class ConditionCheckTests
{
    private static readonly TestError Error = new("CONDITION_ERROR");

    #region That (condition must be true)

    [Fact]
    public void That_WithFalse_ReturnsFailure()
    {
        var result = Result.Check.That(false, Error);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CONDITION_ERROR");
    }

    [Fact]
    public void That_WithTrue_ReturnsSuccess()
    {
        var result = Result.Check.That(true, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void That_WithFactory_WithFalse_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.That(false, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void That_WithFactory_WithTrue_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.That(true, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Not (condition must be false)

    [Fact]
    public void Not_WithTrue_ReturnsFailure()
    {
        var result = Result.Check.Not(true, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Not_WithFalse_ReturnsSuccess()
    {
        var result = Result.Check.Not(false, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotEmpty (Guid)

    [Fact]
    public void NotEmpty_WithEmptyGuid_ReturnsFailure()
    {
        var result = Result.Check.NotEmpty(Guid.Empty, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotEmpty_WithGuid_ReturnsSuccess()
    {
        var result = Result.Check.NotEmpty(Guid.NewGuid(), Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Defined (Enum)

    private enum TestEnum
    {
        Value1 = 1,
        Value2 = 2
    }

    [Fact]
    public void Defined_WithUndefinedValue_ReturnsFailure()
    {
        var value = (TestEnum)999;

        var result = Result.Check.Defined(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Defined_WithDefinedValue_ReturnsSuccess()
    {
        var value = TestEnum.Value1;

        var result = Result.Check.Defined(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region DateTime Checks

    [Fact]
    public void InPast_DateTime_WithPast_ReturnsSuccess()
    {
        var result = Result.Check.InPast(DateTime.UtcNow.AddDays(-1), Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InPast_DateTime_WithFuture_ReturnsFailure()
    {
        var result = Result.Check.InPast(DateTime.UtcNow.AddDays(1), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InFuture_DateTime_WithFuture_ReturnsSuccess()
    {
        var result = Result.Check.InFuture(DateTime.UtcNow.AddDays(1), Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InFuture_DateTime_WithPast_ReturnsFailure()
    {
        var result = Result.Check.InFuture(DateTime.UtcNow.AddDays(-1), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_DateTime_WithDefault_ReturnsFailure()
    {
        var result = Result.Check.NotDefault(default, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_DateTime_WithValue_ReturnsSuccess()
    {
        var result = Result.Check.NotDefault(DateTime.UtcNow, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InPast_DateTimeOffset_WithPast_ReturnsSuccess()
    {
        var result = Result.Check.InPast(DateTimeOffset.UtcNow.AddDays(-1), Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InPast_DateTimeOffset_WithFuture_ReturnsFailure()
    {
        var result = Result.Check.InPast(DateTimeOffset.UtcNow.AddDays(1), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void InFuture_DateTimeOffset_WithFuture_ReturnsSuccess()
    {
        var result = Result.Check.InFuture(DateTimeOffset.UtcNow.AddDays(1), Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void InFuture_DateTimeOffset_WithPast_ReturnsFailure()
    {
        var result = Result.Check.InFuture(DateTimeOffset.UtcNow.AddDays(-1), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_DateTimeOffset_WithDefault_ReturnsFailure()
    {
        var result = Result.Check.NotDefault(default(DateTimeOffset), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_DateTimeOffset_WithValue_ReturnsSuccess()
    {
        var result = Result.Check.NotDefault(DateTimeOffset.UtcNow, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Equality

    [Fact]
    public void Equal_WithDifferentValues_ReturnsFailure()
    {
        var result = Result.Check.Equal(1, 2, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Equal_WithSameValues_ReturnsSuccess()
    {
        var result = Result.Check.Equal(42, 42, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotEqual_WithSameValues_ReturnsFailure()
    {
        var result = Result.Check.NotEqual(42, 42, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotEqual_WithDifferentValues_ReturnsSuccess()
    {
        var result = Result.Check.NotEqual(1, 2, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotDefault (Generic Struct)

    [Fact]
    public void NotDefault_Int_WithDefault_ReturnsFailure()
    {
        var result = Result.Check.NotDefault<int, TestError>(0, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_Int_WithValue_ReturnsSuccess()
    {
        var result = Result.Check.NotDefault<int, TestError>(42, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_Guid_WithEmpty_ReturnsFailure()
    {
        var result = Result.Check.NotDefault<Guid, TestError>(Guid.Empty, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_Generic_WithFactory_WithDefault_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotDefault<int, TestError>(0, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotDefault_Generic_WithFactory_WithValue_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.NotDefault<int, TestError>(42, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion
}