using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class ContainsNullAndSizeTests
{
    // =========================================================================
    // ThrowIfContainsNull — IEnumerable<T?>
    // =========================================================================

    [Fact]
    public void ThrowIfContainsNull_WithNull_ThrowsArgumentNullException()
    {
        IEnumerable<string?>? collection = null;

        var act = () => Ensure.ThrowIfContainsNull(collection);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfContainsNull_WithNullElement_ThrowsArgumentException()
    {
        var collection = new[] { "a", null, "b" };

        var act = () => Ensure.ThrowIfContainsNull(collection.AsEnumerable());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*null elements*");
    }

    [Fact]
    public void ThrowIfContainsNull_WithAllNonNull_DoesNotThrow()
    {
        var collection = new[] { "a", "b", "c" };

        var act = () => Ensure.ThrowIfContainsNull(collection.AsEnumerable());

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfContainsNull_EmptyCollection_DoesNotThrow()
    {
        var collection = Array.Empty<string?>();

        var act = () => Ensure.ThrowIfContainsNull(collection.AsEnumerable());

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfContainsNull_FirstElementNull_ThrowsImmediately()
    {
        var collection = new[] { (string?)null, "b" };

        var act = () => Ensure.ThrowIfContainsNull(collection.AsEnumerable());

        act.Should().Throw<ArgumentException>();
    }

    // =========================================================================
    // ThrowIfContainsNull — Array overload
    // =========================================================================

    [Fact]
    public void ThrowIfContainsNull_Array_WithNull_ThrowsArgumentNullException()
    {
        string?[]? array = null;

        var act = () => Ensure.ThrowIfContainsNull(array);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfContainsNull_Array_WithNullElement_ThrowsArgumentException()
    {
        var array = new[] { "a", null, "b" };

        var act = () => Ensure.ThrowIfContainsNull(array);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfContainsNull_Array_AllValid_DoesNotThrow()
    {
        var array = new[] { "a", "b" };

        var act = () => Ensure.ThrowIfContainsNull(array);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfCountGreaterThan — IReadOnlyCollection
    // =========================================================================

    [Fact]
    public void ThrowIfCountGreaterThan_WithNull_ThrowsArgumentNullException()
    {
        IReadOnlyCollection<int>? collection = null;

        var act = () => Ensure.ThrowIfCountGreaterThan(collection, 5);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfCountGreaterThan_ExceedsMax_ThrowsArgumentOutOfRangeException()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3, 4, 5, 6 };

        var act = () => Ensure.ThrowIfCountGreaterThan(collection, 5);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*must not exceed 5*");
    }

    [Fact]
    public void ThrowIfCountGreaterThan_AtMax_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3, 4, 5 };

        var act = () => Ensure.ThrowIfCountGreaterThan(collection, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfCountGreaterThan_BelowMax_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2 };

        var act = () => Ensure.ThrowIfCountGreaterThan(collection, 5);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfCountLessThan — IReadOnlyCollection
    // =========================================================================

    [Fact]
    public void ThrowIfCountLessThan_WithNull_ThrowsArgumentNullException()
    {
        IReadOnlyCollection<int>? collection = null;

        var act = () => Ensure.ThrowIfCountLessThan(collection, 2);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfCountLessThan_BelowMin_ThrowsArgumentOutOfRangeException()
    {
        IReadOnlyCollection<int> collection = new[] { 1 };

        var act = () => Ensure.ThrowIfCountLessThan(collection, 2);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*at least 2*");
    }

    [Fact]
    public void ThrowIfCountLessThan_AtMin_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2 };

        var act = () => Ensure.ThrowIfCountLessThan(collection, 2);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfCountLessThan_AboveMin_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfCountLessThan(collection, 2);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfCountGreaterThan — Array overload
    // =========================================================================

    [Fact]
    public void ThrowIfCountGreaterThan_Array_ExceedsMax_Throws()
    {
        var array = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfCountGreaterThan(array, 2);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfCountGreaterThan_Array_AtMax_DoesNotThrow()
    {
        var array = new[] { 1, 2 };

        var act = () => Ensure.ThrowIfCountGreaterThan(array, 2);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfCountLessThan — Array overload
    // =========================================================================

    [Fact]
    public void ThrowIfCountLessThan_Array_BelowMin_Throws()
    {
        var array = new[] { 1 };

        var act = () => Ensure.ThrowIfCountLessThan(array, 3);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfCountLessThan_Array_AtMin_DoesNotThrow()
    {
        var array = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfCountLessThan(array, 3);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfCountOutOfRange — IReadOnlyCollection
    // =========================================================================

    [Fact]
    public void ThrowIfCountOutOfRange_WithNull_ThrowsArgumentNullException()
    {
        IReadOnlyCollection<int>? collection = null;

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 1, 5);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_BelowMin_ThrowsArgumentOutOfRangeException()
    {
        IReadOnlyCollection<int> collection = new[] { 1 };

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 2, 5);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*between 2 and 5*");
    }

    [Fact]
    public void ThrowIfCountOutOfRange_AboveMax_ThrowsArgumentOutOfRangeException()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3, 4, 5, 6 };

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 2, 5);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*between 2 and 5*");
    }

    [Fact]
    public void ThrowIfCountOutOfRange_AtMin_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2 };

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 2, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_AtMax_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3, 4, 5 };

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 2, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_InRange_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 2, 5);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_EmptyCollectionBelowMin_Throws()
    {
        IReadOnlyCollection<int> collection = Array.Empty<int>();

        var act = () => Ensure.ThrowIfCountOutOfRange(collection, 1, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // =========================================================================
    // ThrowIfCountOutOfRange — Array overload
    // =========================================================================

    [Fact]
    public void ThrowIfCountOutOfRange_Array_WithNull_ThrowsArgumentNullException()
    {
        int[]? array = null;

        var act = () => Ensure.ThrowIfCountOutOfRange(array, 1, 5);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_Array_BelowMin_Throws()
    {
        var array = new[] { 1 };

        var act = () => Ensure.ThrowIfCountOutOfRange(array, 2, 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_Array_AboveMax_Throws()
    {
        var array = new[] { 1, 2, 3, 4, 5, 6 };

        var act = () => Ensure.ThrowIfCountOutOfRange(array, 2, 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfCountOutOfRange_Array_InRange_DoesNotThrow()
    {
        var array = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfCountOutOfRange(array, 2, 5);

        act.Should().NotThrow();
    }

    // =========================================================================
    // ThrowIfNotDefined<TEnum>
    // =========================================================================

    [Fact]
    public void ThrowIfNotDefined_ValidValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotDefined(DayOfWeek.Monday);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNotDefined_InvalidValue_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Ensure.ThrowIfNotDefined((DayOfWeek)99);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*not a defined member of DayOfWeek*");
    }

    [Fact]
    public void ThrowIfNotDefined_ZeroEnum_WhenNotDefined_Throws()
    {
        var act = () => Ensure.ThrowIfNotDefined((TestNonZeroEnum)0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ThrowIfNotDefined_AllValidValues_DoNotThrow()
    {
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var act = () => Ensure.ThrowIfNotDefined(day);
            act.Should().NotThrow();
        }
    }
}

file enum TestNonZeroEnum
{
    First = 1,
    Second = 2
}
