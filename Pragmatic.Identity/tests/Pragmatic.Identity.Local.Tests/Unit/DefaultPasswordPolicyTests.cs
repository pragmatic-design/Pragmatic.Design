using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Identity.Local.Errors;
using Pragmatic.Identity.Local.Services;

namespace Pragmatic.Identity.Local.Tests.Unit;

public sealed class DefaultPasswordPolicyTests
{
    [Fact]
    public void Validate_WithValidPassword_ReturnsSuccess()
    {
        var policy = CreatePolicy(minLength: 8);

        var result = policy.Validate("SecurePass123!");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithExactMinLength_ReturnsSuccess()
    {
        var policy = CreatePolicy(minLength: 8);

        var result = policy.Validate("12345678");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithShortPassword_ReturnsError()
    {
        var policy = CreatePolicy(minLength: 8);

        var result = policy.Validate("short");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    [Fact]
    public void Validate_WithEmptyPassword_ReturnsError()
    {
        var policy = CreatePolicy(minLength: 8);

        var result = policy.Validate(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    [Fact]
    public void Validate_WithNullPassword_ReturnsError()
    {
        var policy = CreatePolicy(minLength: 8);

        var result = policy.Validate(null!);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<PasswordPolicyError>();
    }

    [Fact]
    public void Validate_WithCustomMinLength_UsesConfiguredValue()
    {
        var policy = CreatePolicy(minLength: 12);

        var result = policy.Validate("12345678"); // 8 chars, less than 12

        result.IsSuccess.Should().BeFalse();
    }

    private static DefaultPasswordPolicy CreatePolicy(int minLength = 8)
    {
        var options = Options.Create(new LocalIdentityOptions { MinPasswordLength = minLength });
        return new DefaultPasswordPolicy(options);
    }
}
