using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for basic validation scenarios.
/// </summary>
public class BasicValidationGeneratorTests : ValidationGeneratorTestBase
{
    [Fact]
    public void Generator_WithRequiredAttribute_GeneratesNullCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  public string Email { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate a Validate() method
        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("public ValidationError Validate()");
        generatedSource.Should().Contain("string.IsNullOrEmpty(Email)");
        generatedSource.Should().Contain("\"validation.required\"");

        // Should not have compilation errors
        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithMinLengthAttribute_GeneratesLengthCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [MinLength(2)]
                                  public string Name { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain(".Length < 2");
        generatedSource.Should().Contain("\"validation.minlength\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithRangeAttribute_GeneratesRangeCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateOrderRequest
                              {
                                  [Range(1, 100)]
                                  public int Quantity { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("< 1");
        generatedSource.Should().Contain("> 100");
        generatedSource.Should().Contain("\"validation.range\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithRequiredNonNullableValueTypes_DoesNotEmitInvalidNullGuard()
    {
        // [Required] on a non-nullable value type (DateTime, int, enum) must not emit
        // `if (Prop is null)`, which is invalid C# (CS0037: cannot convert null to a
        // non-nullable value type). A non-nullable value type is always present, so no null
        // guard should be emitted; sibling attribute checks (e.g. [FutureDate]) still apply.
        const string source = """
            using System;
            using Pragmatic.Validation.Attributes;

            namespace TestNamespace;

            public enum Priority { Low, High }

            public partial class BookingCommand
            {
                [Required]
                [FutureDate]
                public DateTime SlotStart { get; init; }

                [Required]
                public int Seats { get; init; }

                [Required]
                public Priority Level { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "BookingCommand.Validator");
        generated.Should().NotBeNull();
        // No `is null` guard for value types — that would be CS0037.
        generated.Should().NotContain("SlotStart is null");
        generated.Should().NotContain("Seats is null");
        generated.Should().NotContain("Level is null");
        // The other (valid) attribute arm is still rendered.
        generated.Should().Contain("SlotStart");
    }

    [Fact]
    public void Generator_WithRequiredGuid_GeneratesEmptyCheck()
    {
        const string source = """
            using System;
            using Pragmatic.Validation.Attributes;

            namespace TestNamespace;

            public partial class EntityRefCommand
            {
                [Required]
                public Guid BookingId { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "EntityRefCommand.Validator");
        generated.Should().NotBeNull();
        // Guid keeps its meaningful presence check against Guid.Empty (never `is null`).
        generated.Should().Contain("BookingId == System.Guid.Empty");
        generated.Should().NotContain("BookingId is null");
    }

    [Fact]
    public void Generator_WithRequiredReferenceAndNullableValueType_StillEmitsPresenceGuard()
    {
        // Skipping the null guard for value types must NOT drop null/empty presence checks for
        // strings or nullable value types.
        const string source = """
            using System;
            using Pragmatic.Validation.Attributes;

            namespace TestNamespace;

            public partial class ProfileCommand
            {
                [Required]
                public string Name { get; init; }

                [Required]
                public DateTime? OptionalDate { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        var generated = GetGeneratedSource(result, "ProfileCommand.Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("string.IsNullOrEmpty(Name)");
        generated.Should().Contain("OptionalDate is null");
    }

    [Fact]
    public void Generator_WithEmailAttribute_GeneratesEmailValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ContactRequest
                              {
                                  [Email]
                                  public string Email { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("EmailAttribute.IsValidEmail");
        generatedSource.Should().Contain("\"validation.email\"");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithMultipleAttributes_GeneratesAllChecks()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  [Email]
                                  [MaxLength(256)]
                                  public string Email { get; init; }

                                  [Required]
                                  [MinLength(2)]
                                  public string Name { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();

        // Email checks
        generatedSource.Should().Contain("string.IsNullOrEmpty(Email)");
        generatedSource.Should().Contain("EmailAttribute.IsValidEmail");
        generatedSource.Should().Contain(".Length > 256");

        // Name checks
        generatedSource.Should().Contain("string.IsNullOrEmpty(Name)");
        generatedSource.Should().Contain(".Length < 2");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_ImplementsISyncValidatorInterface()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial class CreateUserRequest
                              {
                                  [Required]
                                  public string Email { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain(": ISyncValidator");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_NonPartialType_GeneratesNothing()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public class CreateUserRequest
                              {
                                  [Required]
                                  public string Email { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // PRAG0200 is the companion analyzer's; the generator only skips the type.
        HasDiagnostic(result, "PRAG0200").Should().BeFalse();

        // Should not generate code for non-partial type
        var generatedSource = GetGeneratedSource(result, "CreateUserRequest");
        generatedSource.Should().BeNull();
    }

    [Fact]
    public void Generator_StructType_GeneratesValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial struct MoneyRequest
                              {
                                  [Positive]
                                  public decimal Amount { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("partial struct MoneyRequest");
        generatedSource.Should().Contain("<= 0");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_TypeWithNoValidationAttributes_DoesNotGenerate()
    {
        const string source = """
                              namespace TestNamespace;

                              public partial record SimpleRequest
                              {
                                  public string Name { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // Should not generate anything for types without validation attributes
        HasNoGeneratedFiles(result).Should().BeTrue();
    }
}