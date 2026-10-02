// Pragmatic.Validation.Tests - Snapshot Validator Generator Tests
// Snapshot tests for ValidationSourceGenerator output verification.

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Snapshot-based tests for the ValidationSourceGenerator.
///     These tests verify the exact generated output for key validation scenarios.
/// </summary>
public class SnapshotValidatorGeneratorTests : ValidatorGeneratorTestBase
{
    #region Basic Validation Snapshots

    /// <summary>
    ///     Verifies the generated validator for a simple required field.
    /// </summary>
    [Fact]
    public async Task Required_SimpleString_GeneratesCorrectValidator()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated validator for multiple validation attributes.
    /// </summary>
    [Fact]
    public async Task MultipleAttributes_StringProperty_GeneratesAllValidations()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record UserRegistration
                              {
                                  [Required]
                                  [Email]
                                  [MaxLength(255)]
                                  public string Email { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Range Validation Snapshots

    /// <summary>
    ///     Verifies the generated validator for numeric range validation.
    /// </summary>
    [Fact]
    public async Task Range_IntProperty_GeneratesMinMaxValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record QuantityRequest
                              {
                                  [Range(1, 100)]
                                  public int Quantity { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    #region Conditional Validation Snapshots

    /// <summary>
    ///     Verifies the generated validator for RequiredIf conditional validation.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This snapshot held code that could not work, and said so in its own name. The rule was
    ///     wrapped in <c>if (CardNumber is not null)</c> — the guard every rule on a nullable property
    ///     gets — so <c>[RequiredIf]</c> ran only when the value was already there, and its
    ///     <c>IsNullOrEmpty</c> check could never be true inside it. A rule unable to report the one
    ///     case it exists for, confirmed green by a test that read the text and never ran it.
    ///     Measured by <c>EveryRuleIsExecuted</c> in <c>examples/conformance</c>, which sends a body
    ///     with the condition met and the value missing.
    /// </remarks>
    [Fact]
    public async Task RequiredIf_BoolCondition_GeneratesConditionalValidation()
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

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    /// <summary>
    ///     Verifies the generated validator for EqualTo cross-property validation.
    /// </summary>
    [Fact]
    public async Task EqualTo_PasswordConfirmation_GeneratesCrossPropertyValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ChangePasswordRequest
                              {
                                  [Required]
                                  public string NewPassword { get; init; } = "";

                                  [EqualTo(nameof(NewPassword))]
                                  public string ConfirmPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion

    // NOTE: Collection validation ([NotEmpty] on arrays/lists) has a known issue
    // where the generator uses incorrect property (.Count vs .Length).
    // This should be fixed in the generator in a separate PR.
    // See: Arrays use .Length, List/ICollection use .Count

    #region Nested Validation Snapshots

    /// <summary>
    ///     Verifies the generated validator for nested validatable objects.
    /// </summary>
    [Fact]
    public async Task ValidateElements_NestedDto_GeneratesNestedValidation()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AddressDto
                              {
                                  [Required]
                                  public string Street { get; init; } = "";

                                  [Required]
                                  public string City { get; init; } = "";
                              }

                              public partial record CustomerRequest
                              {
                                  [Required]
                                  public string Name { get; init; } = "";

                                  [ValidateElements]
                                  public List<AddressDto> Addresses { get; init; } = new();
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetAllGeneratedSources(result);
        await Verify(sources);
    }

    #endregion
}
