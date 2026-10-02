using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Tests.ThrowIf;

public class StringTests
{
    #region ThrowIfNullOrEmpty

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ThrowIfNullOrEmpty_WithInvalidValue_ThrowsArgumentException(string? value)
    {
        var act = () => Ensure.ThrowIfNullOrEmpty(value);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("test")]
    public void ThrowIfNullOrEmpty_WithValidValue_DoesNotThrow(string value)
    {
        var act = () => Ensure.ThrowIfNullOrEmpty(value);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_WithValidValue_ReturnsValue()
    {
        var result = Ensure.ThrowIfNullOrEmpty("test");

        result.Should().Be("test");
    }

    [Fact]
    public void ThrowIfNullOrEmpty_EnablesFluentAssignment()
    {
        string name = Ensure.ThrowIfNullOrEmpty("hello");

        name.Should().Be("hello");
    }

    #endregion

    #region ThrowIfNullOrWhiteSpace

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ThrowIfNullOrWhiteSpace_WithInvalidValue_ThrowsArgumentException(string? value)
    {
        var act = () => Ensure.ThrowIfNullOrWhiteSpace(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNullOrWhiteSpace_WithValidValue_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNullOrWhiteSpace("test");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNullOrWhiteSpace_WithValidValue_ReturnsValue()
    {
        var result = Ensure.ThrowIfNullOrWhiteSpace("test");

        result.Should().Be("test");
    }

    [Fact]
    public void ThrowIfNullOrWhiteSpace_EnablesFluentAssignment()
    {
        string name = Ensure.ThrowIfNullOrWhiteSpace("hello");

        name.Should().Be("hello");
    }

    #endregion

    #region ThrowIfLongerThan

    [Fact]
    public void ThrowIfLongerThan_WithLongerString_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfLongerThan("hello world", 5);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at most 5*Actual: 11*");
    }

    [Fact]
    public void ThrowIfLongerThan_WithShorterString_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfLongerThan("test", 10);

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfLongerThan_WithExactLength_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfLongerThan("test", 4);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfShorterThan

    [Fact]
    public void ThrowIfShorterThan_WithShorterString_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfShorterThan("ab", 5);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*at least 5*Actual: 2*");
    }

    [Fact]
    public void ThrowIfShorterThan_WithLongerString_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfShorterThan("hello world", 5);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfLengthOutOfRange

    [Fact]
    public void ThrowIfLengthOutOfRange_WithNull_ThrowsArgumentNullException()
    {
        var act = () => Ensure.ThrowIfLengthOutOfRange(null!, 1, 10);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("a", 2, 5)] // Too short
    [InlineData("abcdef", 2, 5)] // Too long
    public void ThrowIfLengthOutOfRange_WithOutOfRange_ThrowsArgumentException(string value, int min, int max)
    {
        var act = () => Ensure.ThrowIfLengthOutOfRange(value, min, max);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ab", 2, 5)] // Min boundary
    [InlineData("abc", 2, 5)] // Middle
    [InlineData("abcde", 2, 5)] // Max boundary
    public void ThrowIfLengthOutOfRange_WithInRange_DoesNotThrow(string value, int min, int max)
    {
        var act = () => Ensure.ThrowIfLengthOutOfRange(value, min, max);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNotEmail

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@domain.org")]
    [InlineData("a@b.co")]
    public void ThrowIfNotEmail_WithValidEmail_DoesNotThrow(string email)
    {
        var act = () => Ensure.ThrowIfNotEmail(email);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("@nodomain.com")]
    [InlineData("spaces in@email.com")]
    public void ThrowIfNotEmail_WithInvalidEmail_ThrowsArgumentException(string email)
    {
        var act = () => Ensure.ThrowIfNotEmail(email);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNotEmail_WithNull_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotEmail(null);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNotUrl

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?query=1")]
    public void ThrowIfNotUrl_WithValidUrl_DoesNotThrow(string url)
    {
        var act = () => Ensure.ThrowIfNotUrl(url);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    public void ThrowIfNotUrl_WithInvalidUrl_ThrowsArgumentException(string url)
    {
        var act = () => Ensure.ThrowIfNotUrl(url);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNotUrl_WithHttpsRequired_RejectsHttp()
    {
        var act = () => Ensure.ThrowIfNotUrl("http://example.com", true);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNotUrl_WithHttpsRequired_AcceptsHttps()
    {
        var act = () => Ensure.ThrowIfNotUrl("https://example.com", true);

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNotMatch

    [Fact]
    public void ThrowIfNotMatch_WithMatchingPattern_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotMatch("ABC123", @"^[A-Z]+\d+$");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNotMatch_WithNonMatchingPattern_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfNotMatch("abc123", @"^[A-Z]+\d+$");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNotMatch_WithNull_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotMatch(null, @"^test$");

        act.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNotPhone

    [Theory]
    [InlineData("+1234567890")]
    [InlineData("(123) 456-7890")]
    [InlineData("123-456-7890")]
    [InlineData("+1 (123) 456-7890")]
    public void ThrowIfNotPhone_WithValidPhone_DoesNotThrow(string phone)
    {
        var act = () => Ensure.ThrowIfNotPhone(phone);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123abc456")]
    public void ThrowIfNotPhone_WithInvalidPhone_ThrowsArgumentException(string phone)
    {
        var act = () => Ensure.ThrowIfNotPhone(phone);

        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ThrowIfContains

    [Fact]
    public void ThrowIfContains_WithSubstring_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfContains("hello world", "world");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must not contain*world*");
    }

    [Fact]
    public void ThrowIfContains_WithoutSubstring_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfContains("hello world", "xyz");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfContains_WithNull_ThrowsArgumentNullException()
    {
        var act = () => Ensure.ThrowIfContains(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ThrowIfStartsWith

    [Fact]
    public void ThrowIfStartsWith_WithPrefix_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfStartsWith("hello world", "hello");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must not start with*hello*");
    }

    [Fact]
    public void ThrowIfStartsWith_WithoutPrefix_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfStartsWith("hello world", "world");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfStartsWith_WithNull_ThrowsArgumentNullException()
    {
        var act = () => Ensure.ThrowIfStartsWith(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ThrowIfEndsWith

    [Fact]
    public void ThrowIfEndsWith_WithSuffix_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfEndsWith("hello world", "world");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must not end with*world*");
    }

    [Fact]
    public void ThrowIfEndsWith_WithoutSuffix_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfEndsWith("hello world", "hello");

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfEndsWith_WithNull_ThrowsArgumentNullException()
    {
        var act = () => Ensure.ThrowIfEndsWith(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ThrowIfNotCreditCard

    [Theory]
    [InlineData("4111111111111111")]      // Visa (16)
    [InlineData("4111 1111 1111 1111")]    // Visa with spaces
    [InlineData("4111-1111-1111-1111")]    // Visa with hyphens
    [InlineData("5555555555554444")]       // Mastercard
    [InlineData("378282246310005")]        // Amex (15)
    [InlineData("4222222222222")]          // Visa (13)
    public void ThrowIfNotCreditCard_WithValidNumber_DoesNotThrow(string value)
    {
        var act = () => Ensure.ThrowIfNotCreditCard(value);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("4111111111111112")]       // bad Luhn checksum
    [InlineData("1234567890123456")]       // bad checksum
    [InlineData("4111")]                   // too short (< 13 digits)
    [InlineData("41111111111111111111")]   // too long (> 19 digits)
    [InlineData("4111abcd11111111")]       // non-digit
    public void ThrowIfNotCreditCard_WithInvalidNumber_ThrowsArgumentException(string value)
    {
        var act = () => Ensure.ThrowIfNotCreditCard(value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ThrowIfNotCreditCard_WithNull_DoesNotThrow()
    {
        var act = () => Ensure.ThrowIfNotCreditCard(null);

        act.Should().NotThrow();
    }

    #endregion

    #region Regex ReDoS timeout

    // A catastrophic-backtracking pattern plus non-matching input blows past the 250ms
    // regex timeout. The ThrowIf* path must surface it as an ArgumentException.
    private const string CatastrophicPattern = "^(a+)+$";
    private static readonly string ReDoSInput = new string('a', 50) + "!";

    [Fact]
    public void ThrowIfNotMatch_WhenRegexTimesOut_ThrowsArgumentException()
    {
        var act = () => Ensure.ThrowIfNotMatch(ReDoSInput, CatastrophicPattern);

        act.Should().Throw<ArgumentException>();
    }

    #endregion
}