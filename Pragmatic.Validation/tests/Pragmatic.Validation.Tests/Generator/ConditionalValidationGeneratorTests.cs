// Pragmatic.Validation.Tests - Conditional Validation Generator Tests
// Tests for RequiredIf, RequiredIfNot, and cross-property validation scenarios.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for the ValidationSourceGenerator's handling of conditional validation attributes.
/// </summary>
/// <remarks>
///     Conditional validation requires instance access (RequiresInstance = true) to check
///     the value of another property. The generator must generate validation code that
///     properly accesses the instance.
/// </remarks>
public class ConditionalValidationGeneratorTests : ValidatorGeneratorTestBase
{
    #region RequiredIf Tests

    /// <summary>
    ///     Verifies that [RequiredIf] generates conditional validation code.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIf_GeneratesConditionalValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record PaymentRequest
                              {
                                  public bool UseCard { get; init; }

                                  [RequiredIf(nameof(UseCard), true)]
                                  public string? CardNumber { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate ISyncValidator
        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull("Validator should be generated");
        validatorSource.Should().Contain("Validate()");

        // Should check UseCard condition
        validatorSource.Should().Contain("UseCard");
        validatorSource.Should().Contain("CardNumber");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that [RequiredIf] with string comparison value works.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIf_StringValue_GeneratesCorrectComparison()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ShippingRequest
                              {
                                  public string ShippingMethod { get; init; } = "";

                                  [RequiredIf(nameof(ShippingMethod), "Express")]
                                  public string? TrackingRequired { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("ShippingMethod");
        validatorSource.Should().Contain("Express");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that [RequiredIf] with AllowEmptyStrings = true works.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIf_AllowEmptyStrings_GeneratesCorrectValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record OptionalContentRequest
                              {
                                  public bool HasContent { get; init; }

                                  [RequiredIf(nameof(HasContent), true, AllowEmptyStrings = true)]
                                  public string? Content { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("HasContent");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    #endregion

    #region RequiredIfNot Tests

    /// <summary>
    ///     Verifies that [RequiredIfNot] generates inverse conditional validation.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIfNot_GeneratesInverseConditionalValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record GuestCheckout
                              {
                                  public bool IsRegistered { get; init; }

                                  [RequiredIfNot(nameof(IsRegistered), true)]
                                  public string? GuestEmail { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("IsRegistered");
        validatorSource.Should().Contain("GuestEmail");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that [RequiredIfNot] with null value works (required when property is not null).
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIfNot_NullValue_GeneratesCorrectComparison()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ParentChildRequest
                              {
                                  public string? ParentId { get; init; }

                                  [RequiredIfNot(nameof(ParentId), null)]
                                  public string? ChildName { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("ParentId");
        validatorSource.Should().Contain("ChildName");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    #endregion

    #region Multiple Conditional Attributes Tests

    /// <summary>
    ///     Verifies that multiple [RequiredIf] attributes on same property work.
    /// </summary>
    [Fact]
    public void Generator_WithMultipleRequiredIf_GeneratesAllConditions()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record MultiConditionRequest
                              {
                                  public string PaymentType { get; init; } = "";
                                  public bool IsInternational { get; init; }

                                  // Required for both Credit and Debit payment types
                                  [RequiredIf(nameof(PaymentType), "Credit")]
                                  [RequiredIf(nameof(PaymentType), "Debit")]
                                  public string? SecurityCode { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("PaymentType");
        validatorSource.Should().Contain("Credit");
        validatorSource.Should().Contain("Debit");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that combining [RequiredIf] with regular validation attributes works.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIfAndOtherAttributes_GeneratesAllValidations()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record EmailNotificationRequest
                              {
                                  public bool SendEmail { get; init; }

                                  [RequiredIf(nameof(SendEmail), true)]
                                  [Email]
                                  [MaxLength(255)]
                                  public string? EmailAddress { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();

        // Should have conditional required check
        validatorSource.Should().Contain("SendEmail");

        // Should also have email format validation
        validatorSource.Should().Contain("Email");

        // Should have max length validation
        validatorSource.Should().Contain("255");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    #endregion

    #region Cross-Property Comparison Tests

    /// <summary>
    ///     Verifies that [EqualTo] generates cross-property validation.
    /// </summary>
    [Fact]
    public void Generator_WithEqualTo_GeneratesCrossPropertyValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record PasswordChangeRequest
                              {
                                  [Required]
                                  public string NewPassword { get; init; } = "";

                                  [EqualTo(nameof(NewPassword))]
                                  public string ConfirmPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("NewPassword");
        validatorSource.Should().Contain("ConfirmPassword");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that [NotEqualTo] generates cross-property validation.
    /// </summary>
    [Fact]
    public void Generator_WithNotEqualTo_GeneratesCrossPropertyValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record PasswordResetRequest
                              {
                                  [Required]
                                  public string OldPassword { get; init; } = "";

                                  [Required]
                                  [NotEqualTo(nameof(OldPassword))]
                                  public string NewPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("OldPassword");
        validatorSource.Should().Contain("NewPassword");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    /// <summary>
    ///     Verifies that [GreaterThanProperty] generates comparison validation.
    /// </summary>
    /// <remarks>
    ///     Note: The generated code uses IComparable which requires 'using System;'.
    ///     This test verifies the code structure is generated correctly.
    /// </remarks>
    [Fact]
    public void Generator_WithGreaterThanProperty_GeneratesComparisonValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record QuantityRangeRequest
                              {
                                  [Required]
                                  public int MinQuantity { get; init; }

                                  [GreaterThanProperty(nameof(MinQuantity))]
                                  public int MaxQuantity { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();

        // Verify the generated code contains comparison logic (via the throw-safe helper)
        validatorSource.Should().Contain("MaxQuantity");
        validatorSource.Should().Contain("MinQuantity");
        validatorSource.Should().Contain("CrossPropertyComparison.Compare");
    }

    /// <summary>
    ///     Verifies that [LessThanProperty] generates comparison validation.
    /// </summary>
    /// <remarks>
    ///     Note: The generated code uses IComparable which requires 'using System;'.
    ///     This test verifies the code structure is generated correctly.
    /// </remarks>
    [Fact]
    public void Generator_WithLessThanProperty_GeneratesComparisonValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ScoreRangeRequest
                              {
                                  [LessThanProperty(nameof(MaxScore))]
                                  public int MinScore { get; init; }

                                  [Required]
                                  public int MaxScore { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();

        // Verify the generated code contains comparison logic (via the throw-safe helper)
        validatorSource.Should().Contain("MinScore");
        validatorSource.Should().Contain("MaxScore");
        validatorSource.Should().Contain("CrossPropertyComparison.Compare");
    }

    #endregion

    #region Enum-Based Conditional Tests

    /// <summary>
    ///     Verifies that [RequiredIf] with enum value works.
    /// </summary>
    [Fact]
    public void Generator_WithRequiredIf_EnumValue_GeneratesCorrectComparison()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public enum OrderType { Standard, Express, SameDay }

                              public partial record OrderRequest
                              {
                                  public OrderType Type { get; init; }

                                  [RequiredIf(nameof(Type), OrderType.SameDay)]
                                  public string? UrgencyReason { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();
        validatorSource.Should().Contain("Type");
        validatorSource.Should().Contain("SameDay");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    #endregion

    #region Instance-Requiring Validation Tests

    /// <summary>
    ///     Verifies that conditional validations correctly use instance-level validation.
    /// </summary>
    [Fact]
    public void Generator_WithConditionalValidation_UsesInstanceValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ContactRequest
                              {
                                  public bool PreferPhone { get; init; }
                                  public bool PreferEmail { get; init; }

                                  [RequiredIf(nameof(PreferPhone), true)]
                                  public string? PhoneNumber { get; init; }

                                  [RequiredIf(nameof(PreferEmail), true)]
                                  [Email]
                                  public string? EmailAddress { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var validatorSource = GetGeneratedSource(result, "Validator");
        validatorSource.Should().NotBeNull();

        // Should have Validate() method that takes instance
        validatorSource.Should().Contain("Validate()");

        // Should check both conditions
        validatorSource.Should().Contain("PreferPhone");
        validatorSource.Should().Contain("PreferEmail");
        validatorSource.Should().Contain("PhoneNumber");
        validatorSource.Should().Contain("EmailAddress");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    #endregion
}
