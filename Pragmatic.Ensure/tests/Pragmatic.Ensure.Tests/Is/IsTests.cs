using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.Is;

public class IsTests
{
    #region Null Checks

    [Fact]
    public void IsNotNull_ReferenceType_WithNull_ReturnsFalse()
    {
        string? value = null;
        Ensure.IsNotNull(value).Should().BeFalse();
    }

    [Fact]
    public void IsNotNull_ReferenceType_WithValue_ReturnsTrue()
    {
        Ensure.IsNotNull("test").Should().BeTrue();
    }

    [Fact]
    public void IsNotNull_NullableValueType_WithNull_ReturnsFalse()
    {
        int? value = null;
        Ensure.IsNotNull(value).Should().BeFalse();
    }

    [Fact]
    public void IsNotNull_NullableValueType_WithValue_ReturnsTrue()
    {
        int? value = 42;
        Ensure.IsNotNull(value).Should().BeTrue();
    }

    #endregion

    #region String Checks

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("test", true)]
    public void IsNotNullOrEmpty_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsNotNullOrEmpty(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("test", true)]
    public void IsNotNullOrWhiteSpace_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsNotNullOrWhiteSpace(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 2, 5, true)] // Null is valid
    [InlineData("a", 2, 5, false)] // Too short
    [InlineData("ab", 2, 5, true)] // Min boundary
    [InlineData("abc", 2, 5, true)] // Middle
    [InlineData("abcde", 2, 5, true)] // Max boundary
    [InlineData("abcdef", 2, 5, false)] // Too long
    public void IsLengthInRange_ReturnsExpected(string? value, int min, int max, bool expected)
    {
        Ensure.IsLengthInRange(value, min, max).Should().Be(expected);
    }

    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void IsEmail_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsEmail(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com", true)]
    [InlineData("invalid", false)]
    [InlineData(null, false)]
    public void IsUrl_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsUrl(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("+1234567890", true)]
    [InlineData("abc", false)]
    [InlineData(null, false)]
    public void IsPhone_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsPhone(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, @"^test$", false)] // Null returns false (consistent with IsEmail/IsPhone)
    [InlineData("test", @"^test$", true)]
    [InlineData("other", @"^test$", false)]
    public void IsMatch_ReturnsExpected(string? value, string pattern, bool expected)
    {
        Ensure.IsMatch(value, pattern).Should().Be(expected);
    }

    #endregion

    #region Numeric Checks - INumber<T>

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsPositive_ReturnsExpected(int value, bool expected)
    {
        Ensure.IsPositive(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void IsNegative_ReturnsExpected(int value, bool expected)
    {
        Ensure.IsNegative(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void IsZero_ReturnsExpected(int value, bool expected)
    {
        Ensure.IsZero(value).Should().Be(expected);
    }

    #endregion

    #region Numeric Checks - IComparable<T>

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, true)]
    public void IsNotZero_ReturnsExpected(int value, bool expected)
    {
        Ensure.IsNotZero(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void IsNotNegative_ReturnsExpected(int value, bool expected)
    {
        Ensure.IsNotNegative(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1, 10, false)] // Below min
    [InlineData(1, 1, 10, true)] // Min boundary
    [InlineData(5, 1, 10, true)] // Middle
    [InlineData(10, 1, 10, true)] // Max boundary
    [InlineData(11, 1, 10, false)] // Above max
    public void IsInRange_ReturnsExpected(int value, int min, int max, bool expected)
    {
        Ensure.IsInRange(value, min, max).Should().Be(expected);
    }

    [Theory]
    [InlineData(5, 10, false)]
    [InlineData(10, 10, true)]
    [InlineData(15, 10, true)]
    public void IsAtLeastMin_ReturnsExpected(int value, int min, bool expected)
    {
        Ensure.IsAtLeastMin(value, min).Should().Be(expected);
    }

    [Theory]
    [InlineData(5, 10, true)]
    [InlineData(10, 10, true)]
    [InlineData(15, 10, false)]
    public void IsAtMostMax_ReturnsExpected(int value, int max, bool expected)
    {
        Ensure.IsAtMostMax(value, max).Should().Be(expected);
    }

    #endregion

    #region Collection Checks

    [Fact]
    public void IsNotNullOrEmpty_Collection_WithNull_ReturnsFalse()
    {
        IEnumerable<int>? collection = null;
        Ensure.IsNotNullOrEmpty(collection).Should().BeFalse();
    }

    [Fact]
    public void IsNotNullOrEmpty_Collection_WithEmpty_ReturnsFalse()
    {
        Ensure.IsNotNullOrEmpty(Enumerable.Empty<int>()).Should().BeFalse();
    }

    [Fact]
    public void IsNotNullOrEmpty_Collection_WithItems_ReturnsTrue()
    {
        Ensure.IsNotNullOrEmpty(new[] { 1, 2, 3 }.AsEnumerable()).Should().BeTrue();
    }

    [Fact]
    public void IsNotNullOrEmpty_Array_WithNull_ReturnsFalse()
    {
        int[]? array = null;
        Ensure.IsNotNullOrEmpty(array).Should().BeFalse();
    }

    [Fact]
    public void IsNotNullOrEmpty_Array_WithItems_ReturnsTrue()
    {
        Ensure.IsNotNullOrEmpty(new[] { 1, 2, 3 }).Should().BeTrue();
    }

    #endregion

    #region HasNoDuplicates

    [Fact]
    public void HasNoDuplicates_WithNull_ReturnsTrue()
    {
        IEnumerable<int>? value = null;
        Ensure.HasNoDuplicates(value).Should().BeTrue();
    }

    [Fact]
    public void HasNoDuplicates_WithNoDuplicates_ReturnsTrue()
    {
        Ensure.HasNoDuplicates(new[] { 1, 2, 3 }.AsEnumerable()).Should().BeTrue();
    }

    [Fact]
    public void HasNoDuplicates_WithDuplicates_ReturnsFalse()
    {
        Ensure.HasNoDuplicates(new[] { 1, 2, 2 }.AsEnumerable()).Should().BeFalse();
    }

    [Fact]
    public void HasNoDuplicates_EmptyCollection_ReturnsTrue()
    {
        Ensure.HasNoDuplicates(Enumerable.Empty<int>()).Should().BeTrue();
    }

    #endregion

    #region Guid Checks

    [Fact]
    public void IsNotEmpty_Guid_WithEmpty_ReturnsFalse()
    {
        Ensure.IsNotEmpty(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsNotEmpty_Guid_WithValue_ReturnsTrue()
    {
        Ensure.IsNotEmpty(Guid.NewGuid()).Should().BeTrue();
    }

    #endregion

    #region DateTime Checks

    [Fact]
    public void IsPast_DateTime_WithPast_ReturnsTrue()
    {
        Ensure.IsPast(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsPast_DateTime_WithFuture_ReturnsFalse()
    {
        Ensure.IsPast(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void IsFuture_DateTime_WithFuture_ReturnsTrue()
    {
        Ensure.IsFuture(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void IsFuture_DateTime_WithPast_ReturnsFalse()
    {
        Ensure.IsFuture(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsNotDefault_DateTime_WithDefault_ReturnsFalse()
    {
        Ensure.IsNotDefault(default).Should().BeFalse();
    }

    [Fact]
    public void IsNotDefault_DateTime_WithValue_ReturnsTrue()
    {
        Ensure.IsNotDefault(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsPast_DateTimeOffset_WithPast_ReturnsTrue()
    {
        Ensure.IsPast(DateTimeOffset.UtcNow.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsFuture_DateTimeOffset_WithFuture_ReturnsTrue()
    {
        Ensure.IsFuture(DateTimeOffset.UtcNow.AddDays(1)).Should().BeTrue();
    }

    #endregion

    #region Equality Checks

    [Fact]
    public void AreEqual_WithEqualValues_ReturnsTrue()
    {
        Ensure.AreEqual(42, 42).Should().BeTrue();
    }

    [Fact]
    public void AreEqual_WithDifferentValues_ReturnsFalse()
    {
        Ensure.AreEqual(42, 43).Should().BeFalse();
    }

    [Fact]
    public void AreNotEqual_WithDifferentValues_ReturnsTrue()
    {
        Ensure.AreNotEqual(42, 43).Should().BeTrue();
    }

    [Fact]
    public void AreNotEqual_WithEqualValues_ReturnsFalse()
    {
        Ensure.AreNotEqual(42, 42).Should().BeFalse();
    }

    [Theory]
    [InlineData("hello world", "world", false)]
    [InlineData("hello world", "xyz", true)]
    [InlineData(null, "test", false)]
    public void DoesNotContain_ReturnsExpected(string? value, string substring, bool expected)
    {
        Ensure.DoesNotContain(value, substring).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world", "hello", false)]
    [InlineData("hello world", "world", true)]
    [InlineData(null, "test", false)]
    public void DoesNotStartWith_ReturnsExpected(string? value, string prefix, bool expected)
    {
        Ensure.DoesNotStartWith(value, prefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world", "world", false)]
    [InlineData("hello world", "hello", true)]
    [InlineData(null, "test", false)]
    public void DoesNotEndWith_ReturnsExpected(string? value, string suffix, bool expected)
    {
        Ensure.DoesNotEndWith(value, suffix).Should().Be(expected);
    }

    #endregion

    #region Generic Default Checks

    [Fact]
    public void IsNotDefault_Int_WithDefault_ReturnsFalse()
    {
        Ensure.IsNotDefault(0).Should().BeFalse();
    }

    [Fact]
    public void IsNotDefault_Int_WithValue_ReturnsTrue()
    {
        Ensure.IsNotDefault(42).Should().BeTrue();
    }

    [Fact]
    public void IsNotDefault_Guid_WithEmpty_ReturnsFalse()
    {
        Ensure.IsNotDefault(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void IsNotDefault_Guid_WithValue_ReturnsTrue()
    {
        Ensure.IsNotDefault(Guid.NewGuid()).Should().BeTrue();
    }

    #endregion

    #region Enum Checks

    public enum TestEnum
    {
        Value1,
        Value2,
        Value3
    }

    [Fact]
    public void IsDefined_WithDefinedValue_ReturnsTrue()
    {
        Ensure.IsDefined(TestEnum.Value1).Should().BeTrue();
    }

    [Fact]
    public void IsDefined_WithUndefinedValue_ReturnsFalse()
    {
        Ensure.IsDefined((TestEnum)999).Should().BeFalse();
    }

    #endregion

    #region Concrete collection types (regression: overload ambiguity must not occur)

    [Fact]
    public void IsNotNullOrEmpty_ConcreteList_WithItems_ReturnsTrue()
    {
        var list = new List<int> { 1, 2, 3 };

        Ensure.IsNotNullOrEmpty(list).Should().BeTrue();
    }

    [Fact]
    public void IsNotNullOrEmpty_ConcreteList_WithEmpty_ReturnsFalse()
    {
        var list = new List<int>();

        Ensure.IsNotNullOrEmpty(list).Should().BeFalse();
    }

    [Fact]
    public void IsNotNullOrEmpty_ConcreteHashSet_WithItems_ReturnsTrue()
    {
        var set = new HashSet<int> { 1 };

        Ensure.IsNotNullOrEmpty(set).Should().BeTrue();
    }

    [Fact]
    public void IsNotNullOrEmpty_LazyEmptySequence_ReturnsFalse()
    {
        // Lazy sequence where TryGetNonEnumeratedCount fails — must still detect emptiness.
        var lazy = Enumerable.Range(0, 0).Where(_ => true);

        Ensure.IsNotNullOrEmpty(lazy).Should().BeFalse();
    }

    #endregion

    #region IsCreditCard

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("378282246310005", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("1234567890123456", false)]
    [InlineData("4111", false)]
    [InlineData("abcd", false)]
    [InlineData(null, false)]
    public void IsCreditCard_ReturnsExpected(string? value, bool expected)
    {
        Ensure.IsCreditCard(value).Should().Be(expected);
    }

    #endregion

    #region Regex ReDoS timeout (Is* must never throw)

    [Fact]
    public void IsMatch_WhenRegexTimesOut_ReturnsFalseWithoutThrowing()
    {
        // Catastrophic backtracking blows past the 250ms timeout. The Is* contract is
        // "never throws": a timeout must be reported as a non-match (false), not an exception.
        var input = new string('a', 50) + "!";

        var act = () => Ensure.IsMatch(input, "^(a+)+$");

        act.Should().NotThrow();
        Ensure.IsMatch(input, "^(a+)+$").Should().BeFalse();
    }

    #endregion
}