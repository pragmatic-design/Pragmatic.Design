using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Attributes;

public class FormatAttributeTests
{
    public class EmailAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new EmailAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_Empty_ReturnsTrue()
        {
            var attr = new EmailAttribute();
            attr.IsValid("").Should().BeTrue();
        }

        [Theory]
        [InlineData("test@example.com")]
        [InlineData("user.name@domain.co.uk")]
        [InlineData("user+tag@gmail.com")]
        [InlineData("user@subdomain.domain.com")]
        public void IsValid_ValidEmail_ReturnsTrue(string email)
        {
            var attr = new EmailAttribute();
            attr.IsValid(email).Should().BeTrue();
        }

        [Theory]
        [InlineData("notanemail")]
        [InlineData("@nodomain.com")]
        [InlineData("noat.com")]
        [InlineData("spaces in@email.com")]
        public void IsValid_InvalidEmail_ReturnsFalse(string email)
        {
            var attr = new EmailAttribute();
            attr.IsValid(email).Should().BeFalse();
        }
    }

    public class PhoneAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new PhoneAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Theory]
        [InlineData("+1 234 567 8900")]
        [InlineData("(234) 567-8900")]
        [InlineData("234-567-8900")]
        [InlineData("+39 02 1234567")]
        [InlineData("1234567890")]
        public void IsValid_ValidPhone_ReturnsTrue(string phone)
        {
            var attr = new PhoneAttribute();
            attr.IsValid(phone).Should().BeTrue();
        }

        [Theory]
        [InlineData("123")] // Too short
        [InlineData("abcdefghij")] // Letters
        public void IsValid_InvalidPhone_ReturnsFalse(string phone)
        {
            var attr = new PhoneAttribute();
            attr.IsValid(phone).Should().BeFalse();
        }

        [Fact]
        public void IsValid_AdversariallyLongInput_ReturnsFalseWithoutStackOverflow()
        {
            // Regression: IsValidPhone used an unbounded stackalloc char[phone.Length].
            // A large adversarial value must be rejected without crashing the process.
            var huge = new string('1', 200_000); // 200k digits — far over the 15-digit limit

            var attr = new PhoneAttribute();

            attr.IsValid(huge).Should().BeFalse();
            PhoneAttribute.IsValidPhone(huge).Should().BeFalse();
        }
    }

    public class UrlAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new UrlAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Theory]
        [InlineData("https://example.com")]
        [InlineData("http://example.com/path")]
        [InlineData("https://example.com:8080/path?query=1")]
        public void IsValid_ValidHttpUrl_ReturnsTrue(string url)
        {
            var attr = new UrlAttribute();
            attr.IsValid(url).Should().BeTrue();
        }

        [Fact]
        public void IsValid_FtpUrl_WithDefaultSchemes_ReturnsFalse()
        {
            var attr = new UrlAttribute();
            attr.IsValid("ftp://example.com").Should().BeFalse();
        }

        [Fact]
        public void IsValid_FtpUrl_WithFtpAllowed_ReturnsTrue()
        {
            var attr = new UrlAttribute { AllowedSchemes = ["http", "https", "ftp"] };
            attr.IsValid("ftp://example.com").Should().BeTrue();
        }

        [Fact]
        public void IsValid_InvalidUrl_ReturnsFalse()
        {
            var attr = new UrlAttribute();
            attr.IsValid("not a url").Should().BeFalse();
        }

        /// <summary>
        ///     On Linux and macOS <c>Uri.TryCreate("/docs/page", UriKind.Absolute, …)</c> succeeds as
        ///     file:///docs/page, so the relative branch was never reached and the path failed the scheme
        ///     check; on Windows it passed. Only a Linux run can show this one red.
        /// </summary>
        [Fact]
        public void IsValid_RelativePath_WithRequireAbsoluteFalse_ReturnsTrue_OnEveryOs()
        {
            var attr = new UrlAttribute { RequireAbsolute = false };
            attr.IsValid("/docs/page").Should().BeTrue();
        }

        [Fact]
        public void IsValid_RelativePath_WithDefaults_ReturnsFalse()
        {
            var attr = new UrlAttribute();
            attr.IsValid("/docs/page").Should().BeFalse();
        }
    }

    public class RegexAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new RegexAttribute(@"^\d+$");
            attr.IsValid(null).Should().BeTrue();
        }

        [Fact]
        public void IsValid_MatchingPattern_ReturnsTrue()
        {
            var attr = new RegexAttribute(@"^[A-Z]{2}-\d{4}$");
            attr.IsValid("AB-1234").Should().BeTrue();
        }

        [Fact]
        public void IsValid_NonMatchingPattern_ReturnsFalse()
        {
            var attr = new RegexAttribute(@"^[A-Z]{2}-\d{4}$");
            attr.IsValid("AB-12").Should().BeFalse();
            attr.IsValid("ab-1234").Should().BeFalse();
        }
    }

    public class CreditCardAttributeTests
    {
        [Fact]
        public void IsValid_Null_ReturnsTrue()
        {
            var attr = new CreditCardAttribute();
            attr.IsValid(null).Should().BeTrue();
        }

        [Theory]
        [InlineData("4111111111111111")] // Visa test
        [InlineData("5500000000000004")] // Mastercard test
        [InlineData("4111-1111-1111-1111")] // With dashes
        [InlineData("4111 1111 1111 1111")] // With spaces
        public void IsValid_ValidCard_ReturnsTrue(string card)
        {
            var attr = new CreditCardAttribute();
            attr.IsValid(card).Should().BeTrue();
        }

        [Theory]
        [InlineData("1234567890123456")] // Invalid Luhn
        [InlineData("123")] // Too short
        public void IsValid_InvalidCard_ReturnsFalse(string card)
        {
            var attr = new CreditCardAttribute();
            attr.IsValid(card).Should().BeFalse();
        }
    }
}