using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class CollectionAttributeTests
{
    public class MinCountAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new MinCountAttribute(2);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ListBelowMinimum_ReturnsFalse()
        {
            var attr = new MinCountAttribute(3);
            attr.IsValid(new List<int> { 1, 2 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ListAtMinimum_ReturnsTrue()
        {
            var attr = new MinCountAttribute(3);
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ArrayBelowMinimum_ReturnsFalse()
        {
            var attr = new MinCountAttribute(3);
            attr.IsValid(new[] { 1, 2 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ArrayAtMinimum_ReturnsTrue()
        {
            var attr = new MinCountAttribute(3);
            attr.IsValid(new[] { 1, 2, 3 }).Should().BeTrue();
        }
    }

    public class MaxCountAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new MaxCountAttribute(2);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ListAboveMaximum_ReturnsFalse()
        {
            var attr = new MaxCountAttribute(2);
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ListAtMaximum_ReturnsTrue()
        {
            var attr = new MaxCountAttribute(3);
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeTrue();
        }

        [Fact]
        public void IsValid_ListBelowMaximum_ReturnsTrue()
        {
            var attr = new MaxCountAttribute(5);
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeTrue();
        }
    }

    public class CountAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new CountAttribute(2, 5);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_BelowMinimum_ReturnsFalse()
        {
            var attr = new CountAttribute(2, 5);
            attr.IsValid(new List<int> { 1 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_AboveMaximum_ReturnsFalse()
        {
            var attr = new CountAttribute(2, 5);
            attr.IsValid(new List<int> { 1, 2, 3, 4, 5, 6 }).Should().BeFalse();
        }

        [Fact]
        public void IsValid_InRange_ReturnsTrue()
        {
            var attr = new CountAttribute(2, 5);
            attr.IsValid(new List<int> { 1, 2 }).Should().BeTrue();
            attr.IsValid(new List<int> { 1, 2, 3 }).Should().BeTrue();
            attr.IsValid(new List<int> { 1, 2, 3, 4, 5 }).Should().BeTrue();
        }
    }
}