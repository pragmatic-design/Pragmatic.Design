using Pragmatic.Testing.Assertions;
using Pragmatic.Result;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests;

public class ValidationErrorTests
{
    // =========================================================================
    // IHttpError Implementation
    // =========================================================================

    [Fact]
    public void Code_ReturnsValidationError()
    {
        var error = ValidationError.For("Field", "message.key");
        error.Code.Should().Be("VALIDATION_ERROR");
    }

    /// <summary>
    ///     422, because the request was understood and the rules refuse it.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It was 400, which is the answer for a request that could not be read at all — malformed
    ///     JSON, a string where a number goes. Collapsing the two left every client unable to tell a
    ///     bug in its own code from a message it should show the person. Binding failures still answer
    ///     400: those genuinely are the first kind.
    /// </remarks>
    [Fact]
    public void StatusCode_Returns422()
    {
        var error = ValidationError.For("Field", "message.key");
        error.StatusCode.Should().Be(422);
    }

    [Fact]
    public void Title_ReturnsValidationFailed()
    {
        var error = ValidationError.For("Field", "message.key");
        error.Title.Should().Be("Validation Failed");
    }

    [Fact]
    public void ImplementsIError()
    {
        var error = ValidationError.For("Field", "message.key");
        error.Should().BeAssignableTo<IError>();
    }

    // =========================================================================
    // Result-like Semantics
    // =========================================================================

    [Fact]
    public void Valid_HasIsSuccessTrue()
    {
        var error = ValidationError.Valid;

        error.IsSuccess.Should().BeTrue();
        error.IsFailure.Should().BeFalse();
        error.Count.Should().Be(0);
    }

    [Fact]
    public void WithIssue_HasIsFailureTrue()
    {
        var error = ValidationError.For("Email", "validation.required");

        error.IsSuccess.Should().BeFalse();
        error.IsFailure.Should().BeTrue();
        error.Count.Should().Be(1);
    }

    [Fact]
    public void Match_OnValid_ExecutesOnValid()
    {
        var error = ValidationError.Valid;
        var executed = false;

        error.Match(
            () => executed = true,
            _ => executed = false
        );

        executed.Should().BeTrue();
    }

    [Fact]
    public void Match_OnInvalid_ExecutesOnInvalid()
    {
        var error = ValidationError.For("Email", "validation.required");
        IReadOnlyList<ValidationIssue>? capturedIssues = null;

        error.Match(
            () => { },
            issues => capturedIssues = issues
        );

        capturedIssues.Should().NotBeNull();
        capturedIssues.Should().HaveCount(1);
    }

    [Fact]
    public void Match_WithResult_ReturnsCorrectValue()
    {
        var valid = ValidationError.Valid;
        var invalid = ValidationError.For("Email", "validation.required");

        var validResult = valid.Match(
            () => "valid",
            _ => "invalid"
        );
        var invalidResult = invalid.Match(
            () => "valid",
            issues => $"invalid:{issues.Count}"
        );

        validResult.Should().Be("valid");
        invalidResult.Should().Be("invalid:1");
    }

    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void For_CreatesErrorWithSingleIssue()
    {
        var error = ValidationError.For("Email", "validation.email.required");

        error.Count.Should().Be(1);
        error.Issues.Should().HaveCount(1);
        error.Issues[0].PropertyPath.Should().Be("Email");
        error.Issues[0].MessageKey.Should().Be("validation.email.required");
    }

    [Fact]
    public void Single_CreatesErrorWithSingleIssue()
    {
        var error = ValidationError.For("Email", "validation.email.required");

        error.Count.Should().Be(1);
        error.Issues[0].PropertyPath.Should().Be("Email");
        error.Issues[0].MessageKey.Should().Be("validation.email.required");
    }

    [Fact]
    public void FromIssues_CreatesErrorFromCollection()
    {
        var issues = new[]
        {
            new ValidationIssue("validation.required", "Email"),
            new ValidationIssue("validation.minlength", "Name")
        };

        var error = ValidationError.FromIssues(issues);

        error.Count.Should().Be(2);
        error.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void FromIssues_EmptyCollection_ReturnsValid()
    {
        var error = ValidationError.FromIssues([]);

        error.IsSuccess.Should().BeTrue();
        error.Count.Should().Be(0);
    }

    [Fact]
    public void CollectionExpression_CreatesError()
    {
        ValidationError error =
        [
            new ValidationIssue("validation.required", "Email"),
            new ValidationIssue("validation.format", "Email")
        ];

        error.Count.Should().Be(2);
        error.IsFailure.Should().BeTrue();
    }

    // =========================================================================
    // With() Methods (Builder Collapsed)
    // =========================================================================

    [Fact]
    public void With_AddsIssue()
    {
        var error = ValidationError.Valid
            .WithFor("Email", "validation.required");

        error.Count.Should().Be(1);
        error.Issues[0].MessageKey.Should().Be("validation.required");
        error.Issues[0].PropertyPath.Should().Be("Email");
    }

    [Fact]
    public void WithFor_AddsIssuePropertyFirst()
    {
        var error = ValidationError.Valid
            .WithFor("Email", "validation.required");

        error.Count.Should().Be(1);
        error.Issues[0].PropertyPath.Should().Be("Email");
        error.Issues[0].MessageKey.Should().Be("validation.required");
    }

    [Fact]
    public void With_WithParameters_IncludesParameters()
    {
        var error = ValidationError.Valid
            .WithFor("Name", "validation.minlength", ("min", 3));

        error.Issues[0].Parameters.Should().ContainKey("min");
        error.Issues[0].Parameters!["min"].Should().Be(3);
    }

    [Fact]
    public void WithNested_CreatesNestedPath()
    {
        var error = ValidationError.Valid
            .WithNested("Items", 0, "ProductId", "validation.required");

        error.Issues[0].PropertyPath.Should().Be("Items[0].ProductId");
    }

    [Fact]
    public void With_Multiple_AccumulatesIssues()
    {
        var error = ValidationError.Valid
            .WithFor("Email", "validation.required")
            .WithFor("Email", "validation.format")
            .WithFor("Name", "validation.minlength");

        error.Count.Should().Be(3);
    }

    // =========================================================================
    // Combine
    // =========================================================================

    [Fact]
    public void Combine_TwoErrors_MergesIssues()
    {
        var error1 = ValidationError.For("Email", "validation.required");
        var error2 = ValidationError.For("Name", "validation.minlength");

        var combined = error1.Combine(error2);

        combined.Count.Should().Be(2);
        combined.Issues.Should().Contain(i => i.PropertyPath == "Email");
        combined.Issues.Should().Contain(i => i.PropertyPath == "Name");
    }

    [Fact]
    public void Combine_ValidWithError_ReturnsError()
    {
        var valid = ValidationError.Valid;
        var error = ValidationError.For("Email", "validation.required");

        var combined = valid.Combine(error);

        combined.Count.Should().Be(1);
        combined.Equals(error).Should().BeTrue();
    }

    [Fact]
    public void Combine_ErrorWithValid_ReturnsError()
    {
        var error = ValidationError.For("Email", "validation.required");
        var valid = ValidationError.Valid;

        var combined = error.Combine(valid);

        combined.Count.Should().Be(1);
        combined.Equals(error).Should().BeTrue();
    }

    [Fact]
    public void Combine_IEnumerable_AddsAllIssues()
    {
        var error = ValidationError.For("Email", "validation.required");
        var additionalIssues = new[]
        {
            new ValidationIssue("validation.minlength", "Name"),
            new ValidationIssue("validation.range", "Age")
        };

        var combined = error.Combine(additionalIssues);

        combined.Count.Should().Be(3);
    }

    // =========================================================================
    // ToResult Conversion
    // =========================================================================

    [Fact]
    public void ToResult_Valid_ReturnsSuccess()
    {
        var error = ValidationError.Valid;

        var result = error.ToResult();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void ToResult_WithIssues_ReturnsFailure()
    {
        var error = ValidationError.For("Email", "validation.required");

        var result = error.ToResult();

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Count.Should().Be(1);
    }

    // =========================================================================
    // IReadOnlyCollection
    // =========================================================================

    [Fact]
    public void ImplementsIReadOnlyCollection()
    {
        var error = ValidationError.For("Email", "validation.required");

        error.Should().BeAssignableTo<IReadOnlyCollection<ValidationIssue>>();
    }

    [Fact]
    public void CanEnumerate_Issues()
    {
        var error = ValidationError.Valid
            .WithFor("Email", "validation.required")
            .WithFor("Name", "validation.minlength");

        var issues = new List<ValidationIssue>();
        foreach (var issue in error)
            issues.Add(issue);

        issues.Should().HaveCount(2);
    }

    // =========================================================================
    // Equality
    // =========================================================================

    [Fact]
    public void Equals_SameIssues_ReturnsTrue()
    {
        var error1 = ValidationError.For("Email", "validation.required");
        var error2 = ValidationError.For("Email", "validation.required");

        error1.Equals(error2).Should().BeTrue();
        (error1 == error2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentProperty_ReturnsFalse()
    {
        var error1 = ValidationError.For("Email", "validation.required");
        var error2 = ValidationError.For("Name", "validation.required");

        error1.Equals(error2).Should().BeFalse();
        (error1 != error2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentMessageKey_ReturnsFalse()
    {
        var error1 = ValidationError.For("Email", "validation.required");
        var error2 = ValidationError.For("Email", "validation.format");

        error1.Equals(error2).Should().BeFalse();
    }

    [Fact]
    public void Equals_BothValid_ReturnsTrue()
    {
        var valid1 = ValidationError.Valid;
        var valid2 = default(ValidationError);

        valid1.Equals(valid2).Should().BeTrue();
    }

    // =========================================================================
    // ToString
    // =========================================================================

    [Fact]
    public void ToString_Valid_ReturnsValidString()
    {
        var error = ValidationError.Valid;

        error.ToString().Should().Be("ValidationError.Valid");
    }

    [Fact]
    public void ToString_SingleIssue_ReturnsSingular()
    {
        var error = ValidationError.For("Email", "validation.required");

        error.ToString().Should().Contain("1 issue");
    }

    [Fact]
    public void ToString_MultipleIssues_ReturnsPlural()
    {
        var error = ValidationError.Valid
            .WithFor("Email", "validation.required")
            .WithFor("Name", "validation.minlength");

        error.ToString().Should().Contain("2 issues");
    }
}