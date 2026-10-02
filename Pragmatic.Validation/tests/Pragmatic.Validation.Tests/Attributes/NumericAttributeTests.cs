using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class NumericAttributeTests
{
    public class RangeAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new RangeAttribute(1, 10);
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_BelowMinimum_ReturnsFalse()
        {
            var attr = new RangeAttribute(1, 10);
            attr.IsValid(0).Should().BeFalse();
        }

        [Fact]
        public void IsValid_AboveMaximum_ReturnsFalse()
        {
            var attr = new RangeAttribute(1, 10);
            attr.IsValid(11).Should().BeFalse();
        }

        [Fact]
        public void IsValid_InRange_ReturnsTrue()
        {
            var attr = new RangeAttribute(1, 10);
            attr.IsValid(1).Should().BeTrue();
            attr.IsValid(5).Should().BeTrue();
            attr.IsValid(10).Should().BeTrue();
        }

        [Fact]
        public void IsValid_Decimal_ReturnsTrue()
        {
            var attr = new RangeAttribute(0.0, 100.0);
            attr.IsValid(50.5m).Should().BeTrue();
        }
    }

    public class PositiveAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new PositiveAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_Zero_ReturnsFalse()
        {
            var attr = new PositiveAttribute();
            attr.IsValid(0).Should().BeFalse();
        }

        [Fact]
        public void IsValid_Negative_ReturnsFalse()
        {
            var attr = new PositiveAttribute();
            attr.IsValid(-1).Should().BeFalse();
        }

        [Fact]
        public void IsValid_Positive_ReturnsTrue()
        {
            var attr = new PositiveAttribute();
            attr.IsValid(1).Should().BeTrue();
            attr.IsValid(0.1m).Should().BeTrue();
        }
    }

    public class NegativeAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new NegativeAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_Zero_ReturnsFalse()
        {
            var attr = new NegativeAttribute();
            attr.IsValid(0).Should().BeFalse();
        }

        [Fact]
        public void IsValid_Positive_ReturnsFalse()
        {
            var attr = new NegativeAttribute();
            attr.IsValid(1).Should().BeFalse();
        }

        [Fact]
        public void IsValid_Negative_ReturnsTrue()
        {
            var attr = new NegativeAttribute();
            attr.IsValid(-1).Should().BeTrue();
            attr.IsValid(-0.1m).Should().BeTrue();
        }
    }

    public class GreaterThanAttributeTests
    {
        [Fact]
        public void IsValid_EqualValue_ReturnsFalse()
        {
            var attr = new GreaterThanAttribute(10);
            attr.IsValid(10).Should().BeFalse();
        }

        [Fact]
        public void IsValid_LessValue_ReturnsFalse()
        {
            var attr = new GreaterThanAttribute(10);
            attr.IsValid(9).Should().BeFalse();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsTrue()
        {
            var attr = new GreaterThanAttribute(10);
            attr.IsValid(11).Should().BeTrue();
        }
    }

    public class LessThanAttributeTests
    {
        [Fact]
        public void IsValid_EqualValue_ReturnsFalse()
        {
            var attr = new LessThanAttribute(10);
            attr.IsValid(10).Should().BeFalse();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsFalse()
        {
            var attr = new LessThanAttribute(10);
            attr.IsValid(11).Should().BeFalse();
        }

        [Fact]
        public void IsValid_LessValue_ReturnsTrue()
        {
            var attr = new LessThanAttribute(10);
            attr.IsValid(9).Should().BeTrue();
        }
    }

    public class GreaterThanOrEqualAttributeTests
    {
        [Fact]
        public void IsValid_EqualValue_ReturnsTrue()
        {
            var attr = new GreaterThanOrEqualAttribute(10);
            attr.IsValid(10).Should().BeTrue();
        }

        [Fact]
        public void IsValid_LessValue_ReturnsFalse()
        {
            var attr = new GreaterThanOrEqualAttribute(10);
            attr.IsValid(9).Should().BeFalse();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsTrue()
        {
            var attr = new GreaterThanOrEqualAttribute(10);
            attr.IsValid(11).Should().BeTrue();
        }
    }

    public class LessThanOrEqualAttributeTests
    {
        [Fact]
        public void IsValid_EqualValue_ReturnsTrue()
        {
            var attr = new LessThanOrEqualAttribute(10);
            attr.IsValid(10).Should().BeTrue();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsFalse()
        {
            var attr = new LessThanOrEqualAttribute(10);
            attr.IsValid(11).Should().BeFalse();
        }

        [Fact]
        public void IsValid_LessValue_ReturnsTrue()
        {
            var attr = new LessThanOrEqualAttribute(10);
            attr.IsValid(9).Should().BeTrue();
        }
    }
}