using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class StringAttributeTests
{
    public class MinLengthAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new MinLengthAttribute(5);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_StringBelowMinimum_ReturnsFalse()
        {
            var attr = new MinLengthAttribute(5);
            attr.IsValid("abc").Should().BeFalse();
        }

        [Fact]
        public void IsValid_StringAtMinimum_ReturnsTrue()
        {
            var attr = new MinLengthAttribute(5);
            attr.IsValid("abcde").Should().BeTrue();
        }

        [Fact]
        public void IsValid_StringAboveMinimum_ReturnsTrue()
        {
            var attr = new MinLengthAttribute(5);
            attr.IsValid("abcdefg").Should().BeTrue();
        }

        [Fact]
        public void IsValid_ListBelowMinimum_ReturnsFalse()
        {
            var attr = new MinLengthAttribute(3);
            attr.IsValid(new List<int> { 1, 2 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ListAtMinimum_ReturnsTrue()
        {
            var attr = new MinLengthAttribute(3);
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeTrue();
        }
    }

    public class MaxLengthAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new MaxLengthAttribute(5);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_StringAboveMaximum_ReturnsFalse()
        {
            var attr = new MaxLengthAttribute(5);
            attr.IsValid("abcdefg").Should().BeFalse();
        }

        [Fact]
        public void IsValid_StringAtMaximum_ReturnsTrue()
        {
            var attr = new MaxLengthAttribute(5);
            attr.IsValid("abcde").Should().BeTrue();
        }

        [Fact]
        public void IsValid_StringBelowMaximum_ReturnsTrue()
        {
            var attr = new MaxLengthAttribute(5);
            attr.IsValid("abc").Should().BeTrue();
        }
    }

    public class LengthAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new LengthAttribute(2, 5);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_StringBelowMinimum_ReturnsFalse()
        {
            var attr = new LengthAttribute(2, 5);
            attr.IsValid("a").Should().BeFalse();
        }

        [Fact]
        public void IsValid_StringAboveMaximum_ReturnsFalse()
        {
            var attr = new LengthAttribute(2, 5);
            attr.IsValid("abcdefg").Should().BeFalse();
        }

        [Fact]
        public void IsValid_StringInRange_ReturnsTrue()
        {
            var attr = new LengthAttribute(2, 5);
            attr.IsValid("ab").Should().BeTrue();
            attr.IsValid("abc").Should().BeTrue();
            attr.IsValid("abcde").Should().BeTrue();
        }

        [Fact]
        public void Constructor_MinGreaterThanMax_Throws()
        {
            var act = () => new LengthAttribute(10, 5);
            act.Should().Throw<ArgumentException>();
        }
    }
}