using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class PresenceAttributeTests
{
    public class RequiredAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsFalse()
        {
            var attr = new RequiredAttribute();
            attr.IsValid(null).Should().BeFalse();
        }

        [Fact]
        public void IsValid_EmptyString_ReturnsFalse()
        {
            var attr = new RequiredAttribute();
            attr.IsValid("").Should().BeFalse();
        }

        [Fact]
        public void IsValid_EmptyString_WithAllowEmptyStrings_ReturnsTrue()
        {
            var attr = new RequiredAttribute { AllowEmptyStrings = true };
            attr.IsValid("").Should().BeTrue();
        }

        [Fact]
        public void IsValid_NonEmptyString_ReturnsTrue()
        {
            var attr = new RequiredAttribute();
            attr.IsValid("value").Should().BeTrue();
        }

        [Fact]
        public void IsValid_NonNullObject_ReturnsTrue()
        {
            var attr = new RequiredAttribute();
            attr.IsValid(new object()).Should().BeTrue();
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new RequiredAttribute();
            attr.DefaultMessageKey.Should().Be("validation.required");
        }
    }

    public class NotEmptyAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_EmptyString_ReturnsFalse()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid("").Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonEmptyString_ReturnsTrue()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid("a").Should().BeTrue();
        }

        [Fact]
        public void IsValid_EmptyList_ReturnsFalse()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid(new List<int>()).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonEmptyList_ReturnsTrue()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid(new List<int> { 1 }).Should().BeTrue();
        }

        [Fact]
        public void IsValid_EmptyArray_ReturnsFalse()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid(Array.Empty<int>()).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonEmptyArray_ReturnsTrue()
        {
            var attr = new NotEmptyAttribute();
            attr.IsValid(new[] { 1 }).Should().BeTrue();
        }
    }

    public class NotWhiteSpaceAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new NotWhiteSpaceAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_EmptyString_ReturnsFalse()
        {
            var attr = new NotWhiteSpaceAttribute();
            attr.IsValid("").Should().BeFalse();
        }

        [Fact]
        public void IsValid_WhitespaceOnly_ReturnsFalse()
        {
            var attr = new NotWhiteSpaceAttribute();
            attr.IsValid("   ").Should().BeFalse();
            attr.IsValid("\t\n").Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonWhitespace_ReturnsTrue()
        {
            var attr = new NotWhiteSpaceAttribute();
            attr.IsValid("a").Should().BeTrue();
            attr.IsValid(" a ").Should().BeTrue();
        }
    }
}