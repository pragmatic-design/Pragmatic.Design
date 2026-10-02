using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Result.Tests.Check;

public class CollectionCheckTests
{
    private static readonly TestError Error = new("COLLECTION_ERROR");

    #region NotNullOrEmpty - IEnumerable

    [Fact]
    public void NotNullOrEmpty_IEnumerable_WithNull_ReturnsFailure()
    {
        IEnumerable<int>? value = null;

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IEnumerable_WithEmpty_ReturnsFailure()
    {
        var value = Enumerable.Empty<int>();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IEnumerable_WithItems_ReturnsSuccess()
    {
        var value = new[] { 1, 2, 3 }.AsEnumerable();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrEmpty - IEnumerable - Lazy Factory

    [Fact]
    public void NotNullOrEmpty_IEnumerable_WithFactory_WithNull_CallsFactory()
    {
        IEnumerable<int>? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IEnumerable_WithFactory_WithItems_DoesNotCallFactory()
    {
        var value = new[] { 1, 2, 3 }.AsEnumerable();
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotNullOrEmpty - ICollection

    [Fact]
    public void NotNullOrEmpty_ICollection_WithNull_ReturnsFailure()
    {
        ICollection<int>? value = null;

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_ICollection_WithEmpty_ReturnsFailure()
    {
        ICollection<int> value = new List<int>();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_ICollection_WithItems_ReturnsSuccess()
    {
        ICollection<int> value = new List<int> { 1, 2, 3 };

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrEmpty - ICollection - Lazy Factory

    [Fact]
    public void NotNullOrEmpty_ICollection_WithFactory_WithNull_CallsFactory()
    {
        ICollection<int>? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_ICollection_WithFactory_WithItems_DoesNotCallFactory()
    {
        ICollection<int> value = new List<int> { 1, 2, 3 };
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotNullOrEmpty - Array

    [Fact]
    public void NotNullOrEmpty_Array_WithNull_ReturnsFailure()
    {
        int[]? value = null;

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_Array_WithEmpty_ReturnsFailure()
    {
        var value = Array.Empty<int>();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_Array_WithItems_ReturnsSuccess()
    {
        var value = new[] { 1, 2, 3 };

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrEmpty - Array - Lazy Factory

    [Fact]
    public void NotNullOrEmpty_Array_WithFactory_WithNull_CallsFactory()
    {
        int[]? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_Array_WithFactory_WithItems_DoesNotCallFactory()
    {
        var value = new[] { 1, 2, 3 };
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NotNullOrEmpty - IReadOnlyCollection

    [Fact]
    public void NotNullOrEmpty_IReadOnlyCollection_WithNull_ReturnsFailure()
    {
        IReadOnlyCollection<int>? value = null;

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IReadOnlyCollection_WithEmpty_ReturnsFailure()
    {
        IReadOnlyCollection<int> value = new List<int>().AsReadOnly();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IReadOnlyCollection_WithItems_ReturnsSuccess()
    {
        IReadOnlyCollection<int> value = new List<int> { 1, 2, 3 }.AsReadOnly();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrEmpty - IReadOnlyCollection - Lazy Factory

    [Fact]
    public void NotNullOrEmpty_IReadOnlyCollection_WithFactory_WithNull_CallsFactory()
    {
        IReadOnlyCollection<int>? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_IReadOnlyCollection_WithFactory_WithItems_DoesNotCallFactory()
    {
        IReadOnlyCollection<int> value = new List<int> { 1, 2, 3 }.AsReadOnly();
        var factoryCalled = false;

        var result = Result.Check.NotNullOrEmpty(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region NoDuplicates

    [Fact]
    public void NoDuplicates_WithDuplicates_ReturnsFailure()
    {
        var value = new[] { 1, 2, 2, 3 }.AsEnumerable();

        var result = Result.Check.NoDuplicates(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NoDuplicates_WithNoDuplicates_ReturnsSuccess()
    {
        var value = new[] { 1, 2, 3 }.AsEnumerable();

        var result = Result.Check.NoDuplicates(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NoDuplicates_WithNull_ReturnsSuccess()
    {
        IEnumerable<int>? value = null;

        var result = Result.Check.NoDuplicates(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NoDuplicates - Lazy Factory

    [Fact]
    public void NoDuplicates_WithFactory_WithDuplicates_CallsFactory()
    {
        var value = new[] { 1, 2, 2 }.AsEnumerable();
        var factoryCalled = false;

        var result = Result.Check.NoDuplicates(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NoDuplicates_WithFactory_WithNoDuplicates_DoesNotCallFactory()
    {
        var value = new[] { 1, 2, 3 }.AsEnumerable();
        var factoryCalled = false;

        var result = Result.Check.NoDuplicates(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region ContainsNoNull

    [Fact]
    public void ContainsNoNull_WithNullElement_ReturnsFailure()
    {
        var value = new[] { "a", null, "b" };

        var result = Result.Check.ContainsNoNull(value.AsEnumerable(), Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void ContainsNoNull_AllNonNull_ReturnsSuccess()
    {
        var value = new[] { "a", "b", "c" };

        var result = Result.Check.ContainsNoNull(value.AsEnumerable(), Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ContainsNoNull_EmptyCollection_ReturnsSuccess()
    {
        var value = Array.Empty<string?>();

        var result = Result.Check.ContainsNoNull(value.AsEnumerable(), Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region ContainsNoNull - Lazy Factory

    [Fact]
    public void ContainsNoNull_WithFactory_WithNullElement_CallsFactory()
    {
        var value = new[] { "a", null, "b" };
        var factoryCalled = false;

        var result = Result.Check.ContainsNoNull(value.AsEnumerable(), () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void ContainsNoNull_WithFactory_AllNonNull_DoesNotCallFactory()
    {
        var value = new[] { "a", "b", "c" };
        var factoryCalled = false;

        var result = Result.Check.ContainsNoNull(value.AsEnumerable(), () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region CountNotGreaterThan

    [Fact]
    public void CountNotGreaterThan_ExceedsMax_ReturnsFailure()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2, 3 };

        var result = Result.Check.CountNotGreaterThan(value, 2, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CountNotGreaterThan_AtMax_ReturnsSuccess()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2 };

        var result = Result.Check.CountNotGreaterThan(value, 2, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region CountNotGreaterThan - Lazy Factory

    [Fact]
    public void CountNotGreaterThan_WithFactory_ExceedsMax_CallsFactory()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2, 3 };
        var factoryCalled = false;

        var result = Result.Check.CountNotGreaterThan(value, 2, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void CountNotGreaterThan_WithFactory_AtMax_DoesNotCallFactory()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2 };
        var factoryCalled = false;

        var result = Result.Check.CountNotGreaterThan(value, 2, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region CountNotLessThan

    [Fact]
    public void CountNotLessThan_BelowMin_ReturnsFailure()
    {
        IReadOnlyCollection<int> value = new[] { 1 };

        var result = Result.Check.CountNotLessThan(value, 2, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CountNotLessThan_AtMin_ReturnsSuccess()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2 };

        var result = Result.Check.CountNotLessThan(value, 2, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region CountNotLessThan - Lazy Factory

    [Fact]
    public void CountNotLessThan_WithFactory_BelowMin_CallsFactory()
    {
        IReadOnlyCollection<int> value = new[] { 1 };
        var factoryCalled = false;

        var result = Result.Check.CountNotLessThan(value, 2, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void CountNotLessThan_WithFactory_AtMin_DoesNotCallFactory()
    {
        IReadOnlyCollection<int> value = new[] { 1, 2 };
        var factoryCalled = false;

        var result = Result.Check.CountNotLessThan(value, 2, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Concrete collection types (regression: overload ambiguity must not occur)

    [Fact]
    public void NotNullOrEmpty_ConcreteList_WithItems_ReturnsSuccess()
    {
        var value = new List<int> { 1, 2, 3 };

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_ConcreteList_WithEmpty_ReturnsFailure()
    {
        var value = new List<int>();

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_ConcreteHashSet_WithItems_ReturnsSuccess()
    {
        var value = new HashSet<string> { "a" };

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}