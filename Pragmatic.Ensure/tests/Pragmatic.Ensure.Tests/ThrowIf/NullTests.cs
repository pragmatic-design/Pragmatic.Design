using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class NullTests
{
    #region ThrowIfNull - Reference Types

    [Fact]
    public void ThrowIfNull_ReferenceType_WithNull_ThrowsArgumentNullException()
    {
        string? value = null;

        var act = () => Ensure.ThrowIfNull(value);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ThrowIfNull_ReferenceType_WithValue_DoesNotThrow()
    {
        var value = "test";

        var act = () => Ensure.ThrowIfNull(value);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNull_ReferenceType_WithValue_ReturnsValue()
    {
        var value = "test";

        var result = Ensure.ThrowIfNull(value);

        result.Should().BeSameAs(value);
    }

    [Fact]
    public void ThrowIfNull_ReferenceType_EnablesFluentAssignment()
    {
        // Validates the fluent assignment pattern: _field = Ensure.ThrowIfNull(param);
        object service = new object();

        var assigned = Ensure.ThrowIfNull(service);

        assigned.Should().BeSameAs(service);
    }

    [Fact]
    public void ThrowIfNull_ReferenceType_CapturesParameterName()
    {
        string? myParameter = null;

        var act = () => Ensure.ThrowIfNull(myParameter);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("myParameter");
    }

    #endregion

    #region ThrowIfNull - Nullable Value Types

    [Fact]
    public void ThrowIfNull_NullableValueType_WithNull_ThrowsArgumentNullException()
    {
        int? value = null;

        var act = () => Ensure.ThrowIfNull(value);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("value");
    }

    [Fact]
    public void ThrowIfNull_NullableValueType_WithValue_DoesNotThrow()
    {
        int? value = 42;

        var act = () => Ensure.ThrowIfNull(value);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNull_NullableValueType_WithValue_ReturnsUnwrappedValue()
    {
        int? value = 42;

        int result = Ensure.ThrowIfNull(value);

        result.Should().Be(42);
    }

    [Fact]
    public void ThrowIfNull_NullableValueType_ReturnsUnderlyingType()
    {
        // Verifies that the return type is T, not T? (unwrapped)
        Guid? nullable = Guid.NewGuid();

        Guid unwrapped = Ensure.ThrowIfNull(nullable);

        unwrapped.Should().Be(nullable.Value);
    }

    [Fact]
    public void ThrowIfNull_NullableGuid_WithEmpty_DoesNotThrow()
    {
        // Empty Guid is not null
        Guid? value = Guid.Empty;

        var act = () => Ensure.ThrowIfNull(value);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNull - Generic 2-param Tuple

    [Fact]
    public void ThrowIfNull_TwoParams_WithBothValues_ReturnsTuple()
    {
        var a = "hello";
        var b = new object();

        var (r1, r2) = Ensure.ThrowIfNull(a, b);

        r1.Should().BeSameAs(a);
        r2.Should().BeSameAs(b);
    }

    [Fact]
    public void ThrowIfNull_TwoParams_WithFirstNull_ThrowsArgumentNullException()
    {
        string? a = null;
        var b = new object();

        var act = () => Ensure.ThrowIfNull(a, b);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("a");
    }

    [Fact]
    public void ThrowIfNull_TwoParams_WithSecondNull_ThrowsArgumentNullException()
    {
        var a = "hello";
        object? b = null;

        var act = () => Ensure.ThrowIfNull(a, b);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("b");
    }

    [Fact]
    public void ThrowIfNull_TwoParams_EnablesFluentDestructuring()
    {
        object service1 = new object();
        object service2 = new object();

        var (s1, s2) = Ensure.ThrowIfNull(service1, service2);

        s1.Should().BeSameAs(service1);
        s2.Should().BeSameAs(service2);
    }

    #endregion
}