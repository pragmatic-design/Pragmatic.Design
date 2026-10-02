using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class ComparisonAttributeTests
{
    private class TestModel : IPropertyValueProvider
    {
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public long LongMin { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public object? GetPropertyValue(string propertyName) => propertyName switch
        {
            nameof(Password) => Password,
            nameof(ConfirmPassword) => ConfirmPassword,
            nameof(Min) => Min,
            nameof(Max) => Max,
            nameof(LongMin) => LongMin,
            nameof(StartDate) => StartDate,
            nameof(EndDate) => EndDate,
            _ => null
        };
    }

    public class EqualToAttributeTests
    {
        [Fact]
        public void RequiresInstance_ReturnsTrue()
        {
            var attr = new EqualToAttribute(nameof(TestModel.Password));
            attr.RequiresInstance.Should().BeTrue();
        }

        [Fact]
        public void IsValid_MatchingValues_ReturnsTrue()
        {
            var attr = new EqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = "abc123", ConfirmPassword = "abc123" };
            attr.IsValid("abc123", model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_DifferentValues_ReturnsFalse()
        {
            var attr = new EqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = "abc123", ConfirmPassword = "different" };
            attr.IsValid("different", model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_BothNull_ReturnsTrue()
        {
            var attr = new EqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = null, ConfirmPassword = null };
            attr.IsValid(null, model).Should().BeTrue();
        }
    }

    public class NotEqualToAttributeTests
    {
        [Fact]
        public void RequiresInstance_ReturnsTrue()
        {
            var attr = new NotEqualToAttribute(nameof(TestModel.Password));
            attr.RequiresInstance.Should().BeTrue();
        }

        [Fact]
        public void IsValid_DifferentValues_ReturnsTrue()
        {
            var attr = new NotEqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = "old", ConfirmPassword = "new" };
            attr.IsValid("new", model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_MatchingValues_ReturnsFalse()
        {
            var attr = new NotEqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = "same", ConfirmPassword = "same" };
            attr.IsValid("same", model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_ValueNull_ReturnsTrue()
        {
            var attr = new NotEqualToAttribute(nameof(TestModel.Password));
            var model = new TestModel { Password = "abc" };
            attr.IsValid(null, model).Should().BeTrue();
        }
    }

    public class GreaterThanPropertyAttributeTests
    {
        [Fact]
        public void RequiresInstance_ReturnsTrue()
        {
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.Min));
            attr.RequiresInstance.Should().BeTrue();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsTrue()
        {
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.Min));
            var model = new TestModel { Min = 10, Max = 20 };
            attr.IsValid(20, model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_EqualValue_ReturnsFalse()
        {
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.Min));
            var model = new TestModel { Min = 10, Max = 10 };
            attr.IsValid(10, model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_LessValue_ReturnsFalse()
        {
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.Min));
            var model = new TestModel { Min = 10, Max = 5 };
            attr.IsValid(5, model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_DateGreater_ReturnsTrue()
        {
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.StartDate));
            var model = new TestModel
            {
                StartDate = new DateTime(2024, 1, 1),
                EndDate = new DateTime(2024, 12, 31)
            };
            attr.IsValid(new DateTime(2024, 12, 31), model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_MixedNumericTypes_DoesNotThrowAndComparesNumerically()
        {
            // Regression: comparing an int value to a long property used IComparable.CompareTo,
            // which throws ArgumentException on mismatched runtime types. It must compare numerically.
            var attr = new GreaterThanPropertyAttribute(nameof(TestModel.LongMin));
            var model = new TestModel { LongMin = 10L };

            var act = () => attr.IsValid(20, model); // int value vs long property

            act.Should().NotThrow();
            attr.IsValid(20, model).Should().BeTrue();   // 20 > 10
            attr.IsValid(5, model).Should().BeFalse();   // 5 !> 10
        }
    }

    public class LessThanPropertyAttributeTests
    {
        [Fact]
        public void IsValid_LessValue_ReturnsTrue()
        {
            var attr = new LessThanPropertyAttribute(nameof(TestModel.Max));
            var model = new TestModel { Min = 10, Max = 20 };
            attr.IsValid(10, model).Should().BeTrue();
        }

        [Fact]
        public void IsValid_EqualValue_ReturnsFalse()
        {
            var attr = new LessThanPropertyAttribute(nameof(TestModel.Max));
            var model = new TestModel { Min = 10, Max = 10 };
            attr.IsValid(10, model).Should().BeFalse();
        }

        [Fact]
        public void IsValid_GreaterValue_ReturnsFalse()
        {
            var attr = new LessThanPropertyAttribute(nameof(TestModel.Max));
            var model = new TestModel { Min = 30, Max = 20 };
            attr.IsValid(30, model).Should().BeFalse();
        }
    }

    /// <summary>The inclusive form: the equal value is what separates it from the strict one.</summary>
    public class GreaterThanOrEqualPropertyAttributeTests
    {
        private readonly TestModel _model = new() { Min = 10 };
        private readonly GreaterThanOrEqualPropertyAttribute _attr = new(nameof(TestModel.Min));

        [Fact]
        public void IsValid_EqualValue_ReturnsTrue() => _attr.IsValid(10, _model).Should().BeTrue();

        [Fact]
        public void IsValid_GreaterValue_ReturnsTrue() => _attr.IsValid(11, _model).Should().BeTrue();

        [Fact]
        public void IsValid_LessValue_ReturnsFalse() => _attr.IsValid(9, _model).Should().BeFalse();

        [Fact]
        public void IsValid_SameDay_ReturnsTrue()
        {
            var day = new DateTime(2026, 9, 14);
            new GreaterThanOrEqualPropertyAttribute(nameof(TestModel.StartDate))
                .IsValid(day, new TestModel { StartDate = day }).Should().BeTrue();
        }
    }

    /// <summary>The inclusive form of the upper bound.</summary>
    public class LessThanOrEqualPropertyAttributeTests
    {
        private readonly TestModel _model = new() { Max = 10 };
        private readonly LessThanOrEqualPropertyAttribute _attr = new(nameof(TestModel.Max));

        [Fact]
        public void IsValid_EqualValue_ReturnsTrue() => _attr.IsValid(10, _model).Should().BeTrue();

        [Fact]
        public void IsValid_LessValue_ReturnsTrue() => _attr.IsValid(9, _model).Should().BeTrue();

        [Fact]
        public void IsValid_GreaterValue_ReturnsFalse() => _attr.IsValid(11, _model).Should().BeFalse();
    }
}