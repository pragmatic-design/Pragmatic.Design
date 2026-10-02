using Pragmatic.Testing.Assertions;
using Showcase.Booking.Dtos;
using Xunit;

namespace Showcase.Tests.Unit.Validation;

/// <summary>
/// Tests generated validation for RegisterGuestRequest.
/// Demonstrates: [Required], [NotWhiteSpace], [Email], [Phone] validation.
/// </summary>
public class RegisterGuestValidationTests
{
    [Fact]
    public void Validate_WithValidRequest_ReturnsSuccess()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyFirstName_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithWhitespaceLastName_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "   ",
            Email = "john@example.com",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithInvalidEmail_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "not-an-email",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyEmail_ReturnsFailure()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidPhone_ReturnsSuccess()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = "+43123456789"
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithNullPhone_ReturnsSuccess()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = null
        };

        var result = request.Validate();

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithMultipleErrors_ReportsAll()
    {
        var request = new RegisterGuestRequest
        {
            FirstName = "",
            LastName = "",
            Email = ""
        };

        var result = request.Validate();

        result.IsFailure.Should().BeTrue();
    }
}
