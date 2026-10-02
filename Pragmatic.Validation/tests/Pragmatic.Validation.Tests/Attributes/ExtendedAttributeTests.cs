using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

// Date sub-fixtures (FutureDate/PastDate) read the real system clock; join the
// DateValidation collection so they never run in parallel with tests that override
// the process-wide ValidationTimeProvider.Current static.
[Collection(Pragmatic.Validation.Tests.Unit.DateValidationCollection.Name)]
public class ExtendedAttributeTests
{
    public class GuidAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new GuidAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_Empty_ReturnsTrue()
        {
            var attr = new GuidAttribute();
            attr.IsValid("").Should().BeTrue();
        }

        [Theory]
        [InlineData("550e8400-e29b-41d4-a716-446655440000")] // Standard format with hyphens
        [InlineData("550e8400e29b41d4a716446655440000")] // Without hyphens
        [InlineData("{550e8400-e29b-41d4-a716-446655440000}")] // With braces
        [InlineData("(550e8400-e29b-41d4-a716-446655440000)")] // With parentheses
        [InlineData("00000000-0000-0000-0000-000000000000")] // Empty GUID
        public void IsValid_ValidGuid_ReturnsTrue(string guid)
        {
            var attr = new GuidAttribute();
            attr.IsValid(guid).Should().BeTrue();
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("550e8400-e29b-41d4-a716")] // Truncated
        [InlineData("ZZZZZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZZZZZZZZZ")] // Invalid hex
        [InlineData("12345")]
        public void IsValid_InvalidGuid_ReturnsFalse(string guid)
        {
            var attr = new GuidAttribute();
            attr.IsValid(guid).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonString_ReturnsTrue()
        {
            var attr = new GuidAttribute();
            attr.IsValid(42).Should().BeTrue();
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new GuidAttribute();
            attr.DefaultMessageKey.Should().Be("validation.guid");
        }

        [Fact]
        public void IsValidGuid_StaticMethod_WorksCorrectly()
        {
            GuidAttribute.IsValidGuid("550e8400-e29b-41d4-a716-446655440000").Should().BeTrue();
            GuidAttribute.IsValidGuid("not-a-guid").Should().BeFalse();
            GuidAttribute.IsValidGuid(null).Should().BeTrue();
            GuidAttribute.IsValidGuid("").Should().BeTrue();
        }
    }

    public class ValidEnumAttributeTests
    {
        private enum TestStatus
        {
            Active = 0,
            Inactive = 1,
            Suspended = 2
        }

        [Flags]
        private enum Access
        {
            None = 0,
            Read = 1,
            Write = 2,
            Delete = 4
        }

        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new ValidEnumAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_DefinedValue_ReturnsTrue()
        {
            var attr = new ValidEnumAttribute();
            attr.IsValid(TestStatus.Active).Should().BeTrue();
            attr.IsValid(TestStatus.Inactive).Should().BeTrue();
            attr.IsValid(TestStatus.Suspended).Should().BeTrue();
        }

        [Fact]
        public void IsValid_UndefinedValue_ReturnsFalse()
        {
            var attr = new ValidEnumAttribute();
            attr.IsValid((TestStatus)99).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonEnum_ReturnsTrue()
        {
            var attr = new ValidEnumAttribute();
            attr.IsValid("not an enum").Should().BeTrue();
            attr.IsValid(42).Should().BeTrue();
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new ValidEnumAttribute();
            attr.DefaultMessageKey.Should().Be("validation.enum");
        }

        [Fact]
        public void IsValidEnum_StaticMethod_WorksCorrectly()
        {
            ValidEnumAttribute.IsValidEnum(TestStatus.Active).Should().BeTrue();
            ValidEnumAttribute.IsValidEnum((TestStatus)99).Should().BeFalse();
        }

        [Fact]
        public void IsValid_FlagsCombinationOfDefinedBits_ReturnsTrue()
        {
            // Regression: Enum.IsDefined returns false for a combined [Flags] value even when
            // every bit is defined. A valid combination (Read | Write) must pass.
            var attr = new ValidEnumAttribute();

            attr.IsValid(Access.Read | Access.Write).Should().BeTrue();
            attr.IsValid(Access.Read | Access.Write | Access.Delete).Should().BeTrue();
            ValidEnumAttribute.IsValidEnum(Access.Read | Access.Delete).Should().BeTrue();
        }

        [Fact]
        public void IsValid_FlagsWithUndefinedBit_ReturnsFalse()
        {
            var attr = new ValidEnumAttribute();

            // 8 is not a defined bit (defined bits are 1, 2, 4).
            attr.IsValid((Access)(1 | 8)).Should().BeFalse();
            ValidEnumAttribute.IsValidEnum((Access)16).Should().BeFalse();
        }
    }

    // A [Collection] on the outer class does NOT propagate to nested test classes in xUnit.
    // These date sub-fixtures read the real system clock, so they must individually join the
    // DateValidation collection to avoid running in parallel with tests that override
    // ValidationTimeProvider.Current.
    [Collection(Pragmatic.Validation.Tests.Unit.DateValidationCollection.Name)]
    public class FutureDateAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_FutureDateTime_ReturnsTrue()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(DateTime.UtcNow.AddDays(1)).Should().BeTrue();
        }

        [Fact]
        public void IsValid_PastDateTime_ReturnsFalse()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeFalse();
        }

        [Fact]
        public void IsValid_FutureDateTimeOffset_ReturnsTrue()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(DateTimeOffset.UtcNow.AddDays(1)).Should().BeTrue();
        }

        [Fact]
        public void IsValid_PastDateTimeOffset_ReturnsFalse()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(DateTimeOffset.UtcNow.AddDays(-1)).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonDateTime_ReturnsTrue()
        {
            var attr = new FutureDateAttribute();
            attr.IsValid("not a date").Should().BeTrue();
        }

        [Fact]
        public void IsValid_LocalDateTime_MatchesEquivalentDateTimeOffset()
        {
            // Regression: the DateTime path must honour Kind (convert Local -> UTC), so a Local
            // DateTime and the DateTimeOffset for the SAME instant must yield the same verdict.
            var attr = new FutureDateAttribute();
            var local = DateTime.SpecifyKind(DateTime.Now.AddHours(6), DateTimeKind.Local);
            var sameInstant = new DateTimeOffset(local);

            attr.IsValid(local).Should().Be(attr.IsValid(sameInstant));
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new FutureDateAttribute();
            attr.DefaultMessageKey.Should().Be("validation.future_date");
        }
    }

    // See FutureDateAttributeTests: nested test classes need their own [Collection] to be
    // serialized against ValidationTimeProvider.Current overrides (outer-class [Collection]
    // does not propagate to nested classes).
    [Collection(Pragmatic.Validation.Tests.Unit.DateValidationCollection.Name)]
    public class PastDateAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new PastDateAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_PastDateTime_ReturnsTrue()
        {
            var attr = new PastDateAttribute();
            attr.IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
        }

        [Fact]
        public void IsValid_FutureDateTime_ReturnsFalse()
        {
            var attr = new PastDateAttribute();
            attr.IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
        }

        [Fact]
        public void IsValid_PastDateTimeOffset_ReturnsTrue()
        {
            var attr = new PastDateAttribute();
            attr.IsValid(DateTimeOffset.UtcNow.AddDays(-1)).Should().BeTrue();
        }

        [Fact]
        public void IsValid_FutureDateTimeOffset_ReturnsFalse()
        {
            var attr = new PastDateAttribute();
            attr.IsValid(DateTimeOffset.UtcNow.AddDays(1)).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NonDateTime_ReturnsTrue()
        {
            var attr = new PastDateAttribute();
            attr.IsValid("not a date").Should().BeTrue();
        }

        [Fact]
        public void IsValid_LocalDateTime_MatchesEquivalentDateTimeOffset()
        {
            // Regression: the DateTime path must honour Kind (convert Local -> UTC).
            var attr = new PastDateAttribute();
            var local = DateTime.SpecifyKind(DateTime.Now.AddHours(-6), DateTimeKind.Local);
            var sameInstant = new DateTimeOffset(local);

            attr.IsValid(local).Should().Be(attr.IsValid(sameInstant));
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new PastDateAttribute();
            attr.DefaultMessageKey.Should().Be("validation.past_date");
        }
    }

    public class OneOfAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new OneOfAttribute("draft", "published", "archived");
            attr.IsValid(null).Should().BeTrue();
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("published")]
        [InlineData("archived")]
        public void IsValid_AllowedValue_ReturnsTrue(string value)
        {
            var attr = new OneOfAttribute("draft", "published", "archived");
            attr.IsValid(value).Should().BeTrue();
        }

        [Theory]
        [InlineData("deleted")]
        [InlineData("DRAFT")] // Case-sensitive by default
        [InlineData("unknown")]
        public void IsValid_NotAllowedValue_ReturnsFalse(string value)
        {
            var attr = new OneOfAttribute("draft", "published", "archived");
            attr.IsValid(value).Should().BeFalse();
        }

        [Fact]
        public void IsValid_NumericValues_WorksCorrectly()
        {
            var attr = new OneOfAttribute(1, 2, 3);
            attr.IsValid(1).Should().BeTrue();
            attr.IsValid(2).Should().BeTrue();
            attr.IsValid(4).Should().BeFalse();
        }

        [Fact]
        public void DefaultMessageKey_IsCorrect()
        {
            var attr = new OneOfAttribute("a", "b");
            attr.DefaultMessageKey.Should().Be("validation.oneof");
        }

        [Fact]
        public void AllowedValues_AreStored()
        {
            var attr = new OneOfAttribute("draft", "published", "archived");
            attr.AllowedValues.Should().BeEquivalentTo(new object[] { "draft", "published", "archived" });
        }
    }
}
