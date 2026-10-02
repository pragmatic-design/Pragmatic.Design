using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Result.Tests.Check;

public class NullCheckTests
{
    private static readonly TestError Error = new("NULL_ERROR");

    #region NotNull - Reference Types

    [Fact]
    public void NotNull_ReferenceType_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.NotNull(value, Error);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NULL_ERROR");
    }

    [Fact]
    public void NotNull_ReferenceType_WithValue_ReturnsSuccess()
    {
        var value = "test";

        var result = Result.Check.NotNull(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotNull_ReferenceType_WithFactory_WithNull_CallsFactory()
    {
        string? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNull(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNull_ReferenceType_WithFactory_WithValue_DoesNotCallFactory()
    {
        var value = "test";
        var factoryCalled = false;

        var result = Result.Check.NotNull(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotNull - Nullable Value Types

    [Fact]
    public void NotNull_NullableValueType_WithNull_ReturnsFailure()
    {
        int? value = null;

        var result = Result.Check.NotNull(value, Error);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NULL_ERROR");
    }

    [Fact]
    public void NotNull_NullableValueType_WithValue_ReturnsSuccess()
    {
        int? value = 42;

        var result = Result.Check.NotNull(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotNull_NullableGuid_WithEmpty_ReturnsSuccess()
    {
        // Empty Guid is not null
        Guid? value = Guid.Empty;

        var result = Result.Check.NotNull(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}