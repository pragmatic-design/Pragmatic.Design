using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class CollectionTests
{
    #region ThrowIfEmpty - IEnumerable

    [Fact]
    public void ThrowIfEmpty_IEnumerable_WithEmptyEnumerable_ThrowsArgumentException()
    {
        var collection = Enumerable.Empty<int>();

        var act = () => Ensure.ThrowIfEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_IEnumerable_WithNonEmptyEnumerable_DoesNotThrow()
    {
        var collection = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(collection.AsEnumerable());

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfEmpty - ICollection

    [Fact]
    public void ThrowIfEmpty_ICollection_WithEmpty_ThrowsArgumentException()
    {
        ICollection<int> collection = new List<int>();

        var act = () => Ensure.ThrowIfEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_ICollection_WithItems_DoesNotThrow()
    {
        ICollection<int> collection = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(collection);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfEmpty - IReadOnlyCollection

    [Fact]
    public void ThrowIfEmpty_IReadOnlyCollection_WithEmpty_ThrowsArgumentException()
    {
        IReadOnlyCollection<int> collection = new List<int>();

        var act = () => Ensure.ThrowIfEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_IReadOnlyCollection_WithItems_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(collection);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfEmpty - IList

    [Fact]
    public void ThrowIfEmpty_IList_WithEmpty_ThrowsArgumentException()
    {
        IList<int> list = new List<int>();

        var act = () => Ensure.ThrowIfEmpty(list);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_IList_WithItems_DoesNotThrow()
    {
        IList<int> list = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(list);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfEmpty - Array

    [Fact]
    public void ThrowIfEmpty_Array_WithEmpty_ThrowsArgumentException()
    {
        var array = Array.Empty<int>();

        var act = () => Ensure.ThrowIfEmpty(array);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_Array_WithItems_DoesNotThrow()
    {
        var array = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(array);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfContainsDuplicate

    [Fact]
    public void ThrowIfContainsDuplicate_WithDuplicates_ThrowsArgumentException()
    {
        var collection = new[] { 1, 2, 3, 2 };

        var act = () => Ensure.ThrowIfContainsDuplicate(collection.AsEnumerable());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*duplicate*2*");
    }

    [Fact]
    public void ThrowIfContainsDuplicate_WithNoDuplicates_DoesNotThrow()
    {
        var collection = new[] { 1, 2, 3, 4 };

        var act = () => Ensure.ThrowIfContainsDuplicate(collection.AsEnumerable());

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfContainsDuplicate_EmptyCollection_DoesNotThrow()
    {
        var collection = Enumerable.Empty<int>();

        var act = () => Ensure.ThrowIfContainsDuplicate(collection);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfContainsDuplicate_WithStringDuplicates_ThrowsArgumentException()
    {
        var collection = new[] { "a", "b", "a" };

        var act = () => Ensure.ThrowIfContainsDuplicate(collection.AsEnumerable());

        act.Should().Throw<ArgumentException>()
            .WithMessage("*duplicate*a*");
    }

    [Fact]
    public void ThrowIfContainsDuplicate_SingleElement_DoesNotThrow()
    {
        var collection = new[] { 42 };

        var act = () => Ensure.ThrowIfContainsDuplicate(collection.AsEnumerable());

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty - IEnumerable

    [Fact]
    public void ThrowIfNullOrEmpty_IEnumerable_WithNull_ThrowsArgumentNullException()
    {
        IEnumerable<string>? collection = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IEnumerable_WithEmpty_ThrowsArgumentException()
    {
        var collection = Enumerable.Empty<string>();

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IEnumerable_WithItems_DoesNotThrow()
    {
        IEnumerable<string> collection = new[] { "a", "b" };

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty - ICollection

    [Fact]
    public void ThrowIfNullOrEmpty_ICollection_WithNull_ThrowsArgumentNullException()
    {
        ICollection<int>? collection = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ICollection_WithEmpty_ThrowsArgumentException()
    {
        ICollection<int> collection = new List<int>();

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ICollection_WithItems_DoesNotThrow()
    {
        ICollection<int> collection = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty - Array

    [Fact]
    public void ThrowIfNullOrEmpty_Array_WithNull_ThrowsArgumentNullException()
    {
        int[]? array = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(array);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_Array_WithEmpty_ThrowsArgumentException()
    {
        var array = Array.Empty<int>();

        var act = () => Ensure.ThrowIfNullOrEmpty(array);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_Array_WithItems_DoesNotThrow()
    {
        var array = new[] { 1, 2, 3 };

        var act = () => Ensure.ThrowIfNullOrEmpty(array);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty - IList

    [Fact]
    public void ThrowIfNullOrEmpty_IList_WithNull_ThrowsArgumentNullException()
    {
        IList<int>? list = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(list);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IList_WithEmpty_ThrowsArgumentException()
    {
        IList<int> list = new List<int>();

        var act = () => Ensure.ThrowIfNullOrEmpty(list);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IList_WithItems_DoesNotThrow()
    {
        IList<int> list = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfNullOrEmpty(list);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty - IReadOnlyCollection

    [Fact]
    public void ThrowIfNullOrEmpty_IReadOnlyCollection_WithNull_ThrowsArgumentNullException()
    {
        IReadOnlyCollection<int>? collection = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IReadOnlyCollection_WithEmpty_ThrowsArgumentException()
    {
        IReadOnlyCollection<int> collection = new List<int>();

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_IReadOnlyCollection_WithItems_DoesNotThrow()
    {
        IReadOnlyCollection<int> collection = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfNullOrEmpty(collection);

        act.Should().NotThrow();
    }

    #endregion

    #region Concrete collection types (regression: overload ambiguity must not occur)

    // A concrete List<T>/HashSet<T> implements both ICollection<T> and IReadOnlyCollection<T>.
    // The previous per-interface overloads made these calls ambiguous (CS0121) — the single
    // IEnumerable<T> overload must resolve cleanly for the canonical usage.

    [Fact]
    public void ThrowIfEmpty_ConcreteList_WithEmpty_ThrowsArgumentException()
    {
        var list = new List<int>();

        var act = () => Ensure.ThrowIfEmpty(list);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfEmpty_ConcreteList_WithItems_DoesNotThrow()
    {
        var list = new List<int> { 1, 2, 3 };

        var act = () => Ensure.ThrowIfEmpty(list);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfEmpty_ConcreteHashSet_WithItems_DoesNotThrow()
    {
        var set = new HashSet<int> { 1 };

        var act = () => Ensure.ThrowIfEmpty(set);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ConcreteList_WithEmpty_ThrowsArgumentException()
    {
        var list = new List<int>();

        var act = () => Ensure.ThrowIfNullOrEmpty(list);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ConcreteList_WithNull_ThrowsArgumentNullException()
    {
        List<int>? list = null;

        var act = () => Ensure.ThrowIfNullOrEmpty(list);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_ConcreteHashSet_WithItems_DoesNotThrow()
    {
        var set = new HashSet<string> { "a", "b" };

        var act = () => Ensure.ThrowIfNullOrEmpty(set);

        act.Should().NotThrow();
    }

    #endregion
}