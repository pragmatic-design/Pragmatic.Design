using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Unit;

/// <summary>
///     Tests for <see cref="Optional{T}"/> tri-state wrapper:
///     Undefined (absent), Null (explicit null), Value (concrete).
/// </summary>
public class OptionalTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Factory methods & state
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Undefined_IsUndefined_HasNoValue()
    {
        var opt = Optional<string>.Undefined;

        opt.IsUndefined.Should().BeTrue();
        opt.HasValue.Should().BeFalse();
    }

    [Fact]
    public void Undefined_AccessValue_Throws()
    {
        var opt = Optional<string>.Undefined;

        var act = () => _ = opt.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Null_HasValue_ValueIsNull()
    {
        var opt = Optional<string>.Null;

        opt.HasValue.Should().BeTrue();
        opt.IsUndefined.Should().BeFalse();
        opt.Value.Should().BeNull();
    }

    [Fact]
    public void Of_WithValue_HasValueAndReturnsIt()
    {
        var opt = Optional<string>.Of("hello");

        opt.HasValue.Should().BeTrue();
        opt.IsUndefined.Should().BeFalse();
        opt.Value.Should().Be("hello");
    }

    [Fact]
    public void Of_WithNull_SameAsNull()
    {
        var opt = Optional<string>.Of(null);

        opt.HasValue.Should().BeTrue();
        opt.Value.Should().BeNull();
    }

    [Fact]
    public void Default_IsUndefined()
    {
        Optional<int> opt = default;

        opt.IsUndefined.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Implicit operator
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ImplicitOperator_FromValue_CreatesOf()
    {
        Optional<int> opt = 42;

        opt.HasValue.Should().BeTrue();
        opt.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitOperator_FromNullString_CreatesNull()
    {
        Optional<string> opt = (string?)null;

        opt.HasValue.Should().BeTrue();
        opt.Value.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetValueOrDefault
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetValueOrDefault_Undefined_ReturnsDefault()
    {
        var opt = Optional<int>.Undefined;

        opt.GetValueOrDefault(99).Should().Be(99);
    }

    [Fact]
    public void GetValueOrDefault_HasValue_ReturnsValue()
    {
        var opt = Optional<int>.Of(5);

        opt.GetValueOrDefault(99).Should().Be(5);
    }

    [Fact]
    public void GetValueOrDefault_Null_ReturnsNull()
    {
        var opt = Optional<string>.Null;

        opt.GetValueOrDefault("fallback").Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IfPresent
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void IfPresent_Undefined_DoesNotCallAction()
    {
        var opt = Optional<string>.Undefined;
        var called = false;

        opt.IfPresent(_ => called = true);

        called.Should().BeFalse();
    }

    [Fact]
    public void IfPresent_HasValue_CallsAction()
    {
        var opt = Optional<string>.Of("x");
        string? captured = null;

        opt.IfPresent(v => captured = v);

        captured.Should().Be("x");
    }

    [Fact]
    public void IfPresent_Null_CallsActionWithNull()
    {
        var opt = Optional<string>.Null;
        var called = false;
        string? captured = "not-null";

        opt.IfPresent(v => { called = true; captured = v; });

        called.Should().BeTrue();
        captured.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Map
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Map_Undefined_ReturnsUndefined()
    {
        var opt = Optional<int>.Undefined;

        var mapped = opt.Map(v => v * 2);

        mapped.IsUndefined.Should().BeTrue();
    }

    [Fact]
    public void Map_HasValue_TransformsValue()
    {
        var opt = Optional<int>.Of(5);

        var mapped = opt.Map(v => v * 2);

        mapped.HasValue.Should().BeTrue();
        mapped.Value.Should().Be(10);
    }

    [Fact]
    public void Map_Null_CallsMapperWithNull()
    {
        var opt = Optional<string>.Null;

        var mapped = opt.Map(v => v?.Length ?? -1);

        mapped.HasValue.Should().BeTrue();
        mapped.Value.Should().Be(-1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Equality
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Equals_TwoUndefined_AreEqual()
    {
        var a = Optional<int>.Undefined;
        var b = Optional<int>.Undefined;

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_UndefinedVsValue_NotEqual()
    {
        var a = Optional<int>.Undefined;
        var b = Optional<int>.Of(0);

        a.Equals(b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void Equals_SameValue_Equal()
    {
        var a = Optional<string>.Of("hello");
        var b = Optional<string>.Of("hello");

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValue_NotEqual()
    {
        var a = Optional<int>.Of(1);
        var b = Optional<int>.Of(2);

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_BothNull_Equal()
    {
        var a = Optional<string>.Null;
        var b = Optional<string>.Null;

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_NullVsValue_NotEqual()
    {
        var a = Optional<string>.Null;
        var b = Optional<string>.Of("x");

        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Equals_BoxedObject_Works()
    {
        var a = Optional<int>.Of(42);
        object b = Optional<int>.Of(42);

        a.Equals(b).Should().BeTrue();
    }

    [Fact]
    public void Equals_BoxedDifferentType_ReturnsFalse()
    {
        var a = Optional<int>.Of(42);
        object b = "not an optional";

        a.Equals(b).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetHashCode
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetHashCode_Undefined_ReturnsMinusOne()
    {
        Optional<string>.Undefined.GetHashCode().Should().Be(-1);
    }

    [Fact]
    public void GetHashCode_Null_ReturnsZero()
    {
        Optional<string>.Null.GetHashCode().Should().Be(0);
    }

    [Fact]
    public void GetHashCode_SameValue_SameHash()
    {
        var a = Optional<int>.Of(42);
        var b = Optional<int>.Of(42);

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ToString
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ToString_Undefined_ReturnsUndefined()
    {
        Optional<int>.Undefined.ToString().Should().Be("Undefined");
    }

    [Fact]
    public void ToString_Null_ReturnsOptionalNull()
    {
        Optional<string>.Null.ToString().Should().Be("Optional(null)");
    }

    [Fact]
    public void ToString_Value_ReturnsOptionalValue()
    {
        Optional<int>.Of(42).ToString().Should().Be("Optional(42)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Value types (int, bool, etc.)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueType_Of_Zero_HasValue()
    {
        var opt = Optional<int>.Of(0);

        opt.HasValue.Should().BeTrue();
        opt.Value.Should().Be(0);
    }

    [Fact]
    public void ValueType_Of_False_HasValue()
    {
        var opt = Optional<bool>.Of(false);

        opt.HasValue.Should().BeTrue();
        opt.Value.Should().BeFalse();
    }
}
