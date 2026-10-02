using Pragmatic.Testing.Assertions;

namespace Pragmatic.Ensure.Result.Tests.Check;

public class StringCheckTests
{
    private static readonly TestError Error = new("STRING_ERROR");

    #region NotNullOrEmpty

    [Fact]
    public void NotNullOrEmpty_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_WithEmpty_ReturnsFailure()
    {
        var value = "";

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_WithWhitespace_ReturnsSuccess()
    {
        var value = "   ";

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrEmpty_WithValue_ReturnsSuccess()
    {
        var value = "test";

        var result = Result.Check.NotNullOrEmpty(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrEmpty - Lazy Factory

    [Fact]
    public void NotNullOrEmpty_WithFactory_WithNull_CallsFactory()
    {
        string? value = null;
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
    public void NotNullOrEmpty_WithFactory_WithValue_DoesNotCallFactory()
    {
        var value = "test";
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

    #region NotNullOrWhiteSpace

    [Fact]
    public void NotNullOrWhiteSpace_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.NotNullOrWhiteSpace(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrWhiteSpace_WithEmpty_ReturnsFailure()
    {
        var value = "";

        var result = Result.Check.NotNullOrWhiteSpace(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrWhiteSpace_WithWhitespace_ReturnsFailure()
    {
        var value = "   ";

        var result = Result.Check.NotNullOrWhiteSpace(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrWhiteSpace_WithValue_ReturnsSuccess()
    {
        var value = "test";

        var result = Result.Check.NotNullOrWhiteSpace(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region NotNullOrWhiteSpace - Lazy Factory

    [Fact]
    public void NotNullOrWhiteSpace_WithFactory_WithNull_CallsFactory()
    {
        string? value = null;
        var factoryCalled = false;

        var result = Result.Check.NotNullOrWhiteSpace(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void NotNullOrWhiteSpace_WithFactory_WithValue_DoesNotCallFactory()
    {
        var value = "test";
        var factoryCalled = false;

        var result = Result.Check.NotNullOrWhiteSpace(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region LengthInRange

    [Fact]
    public void LengthInRange_WithNull_ReturnsSuccess()
    {
        // Null is valid (null-safe)
        string? value = null;

        var result = Result.Check.LengthInRange(value, 1, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void LengthInRange_WithTooShort_ReturnsFailure()
    {
        var value = "ab";

        var result = Result.Check.LengthInRange(value, 5, 10, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void LengthInRange_WithTooLong_ReturnsFailure()
    {
        var value = "abcdefghijk"; // 11 chars

        var result = Result.Check.LengthInRange(value, 1, 10, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void LengthInRange_WithInRange_ReturnsSuccess()
    {
        var value = "abcde"; // 5 chars

        var result = Result.Check.LengthInRange(value, 1, 10, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region LengthInRange - Lazy Factory

    [Fact]
    public void LengthInRange_WithFactory_WithTooShort_CallsFactory()
    {
        var value = "ab";
        var factoryCalled = false;

        var result = Result.Check.LengthInRange(value, 5, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void LengthInRange_WithFactory_WithInRange_DoesNotCallFactory()
    {
        var value = "abcde";
        var factoryCalled = false;

        var result = Result.Check.LengthInRange(value, 1, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public void LengthInRange_WithFactory_WithNull_DoesNotCallFactory()
    {
        string? value = null;
        var factoryCalled = false;

        var result = Result.Check.LengthInRange(value, 1, 10, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Email

    [Fact]
    public void Email_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.Email(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Email_WithInvalid_ReturnsFailure()
    {
        var value = "not-an-email";

        var result = Result.Check.Email(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Email_WithValid_ReturnsSuccess()
    {
        var value = "user@example.com";

        var result = Result.Check.Email(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Email - Lazy Factory

    [Fact]
    public void Email_WithFactory_WithInvalid_CallsFactory()
    {
        var value = "not-an-email";
        var factoryCalled = false;

        var result = Result.Check.Email(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void Email_WithFactory_WithValid_DoesNotCallFactory()
    {
        var value = "user@example.com";
        var factoryCalled = false;

        var result = Result.Check.Email(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Url

    [Fact]
    public void Url_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.Url(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Url_WithInvalidUrl_ReturnsFailure()
    {
        var value = "not-a-url";

        var result = Result.Check.Url(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Url_WithHttpUrl_ReturnsSuccess()
    {
        var value = "http://example.com";

        var result = Result.Check.Url(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Url_WithHttpsUrl_ReturnsSuccess()
    {
        var value = "https://example.com";

        var result = Result.Check.Url(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Url_WithHttpsRequired_RejectsHttp()
    {
        var value = "http://example.com";

        var result = Result.Check.Url(value, Error, true);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Url_WithHttpsRequired_AcceptsHttps()
    {
        var value = "https://example.com";

        var result = Result.Check.Url(value, Error, true);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Url - Lazy Factory

    [Fact]
    public void Url_WithFactory_WithInvalid_CallsFactory()
    {
        var value = "not-a-url";
        var factoryCalled = false;

        var result = Result.Check.Url(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void Url_WithFactory_WithValid_DoesNotCallFactory()
    {
        var value = "https://example.com";
        var factoryCalled = false;

        var result = Result.Check.Url(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Phone

    [Fact]
    public void Phone_WithNull_ReturnsFailure()
    {
        string? value = null;

        var result = Result.Check.Phone(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Phone_WithInvalidPhone_ReturnsFailure()
    {
        var value = "not-a-phone";

        var result = Result.Check.Phone(value, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Phone_WithValidPhone_ReturnsSuccess()
    {
        var value = "+1234567890";

        var result = Result.Check.Phone(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Phone_WithFormattedPhone_ReturnsSuccess()
    {
        var value = "(123) 456-7890";

        var result = Result.Check.Phone(value, Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Phone - Lazy Factory

    [Fact]
    public void Phone_WithFactory_WithInvalid_CallsFactory()
    {
        var value = "not-a-phone";
        var factoryCalled = false;

        var result = Result.Check.Phone(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void Phone_WithFactory_WithValid_DoesNotCallFactory()
    {
        var value = "+1234567890";
        var factoryCalled = false;

        var result = Result.Check.Phone(value, () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region Match

    [Fact]
    public void Match_WithNull_ReturnsSuccess()
    {
        // Null is valid (null-safe)
        string? value = null;

        var result = Result.Check.Match(value, "^[A-Z]+$", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Match_WithNoMatch_ReturnsFailure()
    {
        var value = "abc123";

        var result = Result.Check.Match(value, "^[A-Z]+$", Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Match_WithMatch_ReturnsSuccess()
    {
        var value = "ABC";

        var result = Result.Check.Match(value, "^[A-Z]+$", Error);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Match - Lazy Factory

    [Fact]
    public void Match_WithFactory_WithNoMatch_CallsFactory()
    {
        var value = "abc123";
        var factoryCalled = false;

        var result = Result.Check.Match(value, "^[A-Z]+$", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    [Fact]
    public void Match_WithFactory_WithMatch_DoesNotCallFactory()
    {
        var value = "ABC";
        var factoryCalled = false;

        var result = Result.Check.Match(value, "^[A-Z]+$", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsSuccess.Should().BeTrue();
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region DoesNotContain

    [Fact]
    public void DoesNotContain_WithSubstring_ReturnsFailure()
    {
        var result = Result.Check.DoesNotContain("hello world", "world", Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void DoesNotContain_WithoutSubstring_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotContain("hello world", "xyz", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotContain_WithNull_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotContain(null, "test", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotContain_WithFactory_WithSubstring_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.DoesNotContain("hello world", "world", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    #endregion

    #region DoesNotStartWith

    [Fact]
    public void DoesNotStartWith_WithPrefix_ReturnsFailure()
    {
        var result = Result.Check.DoesNotStartWith("hello world", "hello", Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void DoesNotStartWith_WithoutPrefix_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotStartWith("hello world", "world", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotStartWith_WithNull_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotStartWith(null, "test", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotStartWith_WithFactory_WithPrefix_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.DoesNotStartWith("hello world", "hello", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    #endregion

    #region DoesNotEndWith

    [Fact]
    public void DoesNotEndWith_WithSuffix_ReturnsFailure()
    {
        var result = Result.Check.DoesNotEndWith("hello world", "world", Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void DoesNotEndWith_WithoutSuffix_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotEndWith("hello world", "hello", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotEndWith_WithNull_ReturnsSuccess()
    {
        var result = Result.Check.DoesNotEndWith(null, "test", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void DoesNotEndWith_WithFactory_WithSuffix_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.DoesNotEndWith("hello world", "world", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    #endregion

    #region CreditCard

    [Fact]
    public void CreditCard_WithValidNumber_ReturnsSuccess()
    {
        var result = Result.Check.CreditCard("4111111111111111", Error);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CreditCard_WithInvalidNumber_ReturnsFailure()
    {
        var result = Result.Check.CreditCard("4111111111111112", Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CreditCard_WithNull_ReturnsFailure()
    {
        var result = Result.Check.CreditCard(null, Error);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void CreditCard_WithFactory_WithInvalid_CallsFactory()
    {
        var factoryCalled = false;

        var result = Result.Check.CreditCard("bad", () =>
        {
            factoryCalled = true;
            return Error;
        });

        result.IsFailure.Should().BeTrue();
        factoryCalled.Should().BeTrue();
    }

    #endregion
}