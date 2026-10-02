using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

public class FromNullableExtensionsTests
{
    private static readonly StringError TestError = new("Not found");

    // =========================================================================
    // Reference type overloads
    // =========================================================================

    [Fact]
    public void FromNullable_ReferenceType_NonNull_ReturnsSuccess()
    {
        var result = Result.FromNullable<string, StringError>("hello", TestError);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void FromNullable_ReferenceType_Null_ReturnsFailure()
    {
        var result = Result.FromNullable<string, StringError>(null, TestError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TestError);
    }

    [Fact]
    public void FromNullable_ReferenceType_WithFactory_NonNull_DoesNotCallFactory()
    {
        var factoryCalled = false;

        var result = Result.FromNullable<string, StringError>("hello", () =>
        {
            factoryCalled = true;
            return TestError;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void FromNullable_ReferenceType_WithFactory_Null_CallsFactory()
    {
        var result = Result.FromNullable<string, StringError>(null, () => TestError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TestError);
    }

    // =========================================================================
    // Value type overloads
    // =========================================================================

    [Fact]
    public void FromNullable_ValueType_HasValue_ReturnsSuccess()
    {
        int? value = 42;
        var result = Result.FromNullable<int, StringError>(value, TestError);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void FromNullable_ValueType_Null_ReturnsFailure()
    {
        int? value = null;
        var result = Result.FromNullable<int, StringError>(value, TestError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TestError);
    }

    [Fact]
    public void FromNullable_ValueType_WithFactory_HasValue_DoesNotCallFactory()
    {
        var factoryCalled = false;
        Guid? value = Guid.NewGuid();

        var result = Result.FromNullable<Guid, StringError>(value, () =>
        {
            factoryCalled = true;
            return TestError;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void FromNullable_ValueType_WithFactory_Null_CallsFactory()
    {
        DateTime? value = null;
        var result = Result.FromNullable<DateTime, StringError>(value, () => TestError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(TestError);
    }

    // =========================================================================
    // Edge cases
    // =========================================================================

    [Fact]
    public void FromNullable_ReferenceType_EmptyString_ReturnsSuccess()
    {
        // Empty string is not null — should succeed
        var result = Result.FromNullable<string, StringError>("", TestError);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void FromNullable_ValueType_Zero_ReturnsSuccess()
    {
        int? value = 0;
        var result = Result.FromNullable<int, StringError>(value, TestError);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public void FromNullable_ValueType_DefaultGuid_ReturnsSuccess()
    {
        Guid? value = Guid.Empty;
        var result = Result.FromNullable<Guid, StringError>(value, TestError);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(Guid.Empty);
    }
}
