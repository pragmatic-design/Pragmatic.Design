// Pragmatic.Validation.Tests - Validation Diagnostics Tests
// Tests for ValidationSourceGenerator diagnostic emissions (PRAG0200-0209, PRAG0220).

using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for ValidationSourceGenerator diagnostic emissions.
///     Verifies correct diagnostic codes are emitted for various error conditions.
/// </summary>
public class ValidationDiagnosticsTests : ValidatorGeneratorTestBase
{
    #region PRAG0200: TypeMustBePartial

    /// <summary>
    ///     A non-partial type with validation attributes is skipped. PRAG0200 is the companion analyzer's,
    ///     reported on the declaration; the generator has no second copy
    ///     (AMustBePartialDiagnosticHasOneOwnerTests).
    /// </summary>
    [Fact]
    public void TypeMustBePartial_NonPartialClass_IsSkippedWithoutAGeneratorDiagnostic()
    {
        // Note: class is NOT partial
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public record CreateUserRequest
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0200").Should().BeFalse("one ID, one owner: the analyzer reports it");
        HasCompilationErrors(result).Should().BeFalse("nothing was generated into a type that cannot take it");
    }

    /// <summary>
    ///     Verifies no PRAG0200 when the type is correctly declared as partial.
    /// </summary>
    [Fact]
    public void TypeMustBePartial_PartialClass_NoDiagnostic()
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

        HasDiagnostic(result, "PRAG0200").Should().BeFalse(
            "partial type should not emit PRAG0200");
        HasCompilationErrors(result).Should().BeFalse();
    }

    #endregion

    #region PRAG0201: ValidatorMustImplementInterface

    /// <summary>
    ///     Verifies PRAG0201 is emitted when a [Validator] class does not implement IAsyncValidator&lt;T&gt;.
    /// </summary>
    [Fact]
    public void ValidatorMustImplementInterface_NoIAsyncValidator_EmitsPrag0201()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              // [Validator] but does NOT implement IAsyncValidator<T>
                              [Validator]
                              public class OrphanValidator
                              {
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0201").Should().BeTrue(
            "[Validator] class without IAsyncValidator<T> should emit PRAG0201");

        var diagnostics = GetGeneratorDiagnostics(result).ToList();
        diagnostics.Should().Contain(d =>
            d.Id == "PRAG0201" &&
            d.GetMessage().Contains("OrphanValidator"));
    }

    /// <summary>
    ///     Verifies no PRAG0201 when the [Validator] class correctly implements IAsyncValidator&lt;T&gt;.
    /// </summary>
    [Fact]
    public void ValidatorMustImplementInterface_ImplementsIAsyncValidator_NoDiagnostic()
    {
        const string source = """
                              using Pragmatic.Validation;
                              using Pragmatic.Validation.Attributes;
                              using Pragmatic.Validation.Types;
                              using System.Threading;
                              using System.Threading.Tasks;

                              namespace TestNamespace;

                              public partial record CreateUserRequest
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }

                              [Validator]
                              public class CreateUserValidator : IAsyncValidator<CreateUserRequest>
                              {
                                  public Task<ValidationError> ValidateAsync(
                                      CreateUserRequest instance,
                                      CancellationToken ct = default)
                                      => Task.FromResult(ValidationError.Valid);
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0201").Should().BeFalse(
            "validator implementing IAsyncValidator<T> should not emit PRAG0201");
        HasCompilationErrors(result).Should().BeFalse();
    }

    #endregion

    #region PRAG0203: ComparisonPropertyNotFound

    /// <summary>
    ///     Verifies PRAG0203 is emitted when [EqualTo] references a non-existent property.
    /// </summary>
    [Fact]
    public void ComparisonPropertyNotFound_EqualToNonExistent_EmitsPrag0203()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ChangePasswordRequest
                              {
                                  public string Password { get; init; } = "";

                                  [EqualTo("NonExistentProperty")]
                                  public string ConfirmPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeTrue(
            "referencing non-existent property should emit PRAG0203");

        var diagnostics = GetGeneratorDiagnostics(result).ToList();
        diagnostics.Should().Contain(d =>
            d.Id == "PRAG0203" &&
            d.GetMessage().Contains("NonExistentProperty"));
    }

    /// <summary>
    ///     Verifies no PRAG0203 when [EqualTo] references an existing property.
    /// </summary>
    [Fact]
    public void ComparisonPropertyNotFound_ValidReference_NoDiagnostic()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ChangePasswordRequest
                              {
                                  public string Password { get; init; } = "";

                                  [EqualTo(nameof(Password))]
                                  public string ConfirmPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeFalse(
            "valid property reference should not emit PRAG0203");
        HasCompilationErrors(result).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies PRAG0203 is emitted when [GreaterThanProperty] references a non-existent property.
    /// </summary>
    [Fact]
    public void ComparisonPropertyNotFound_GreaterThanNonExistent_EmitsPrag0203()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record DateRangeRequest
                              {
                                  public DateTime StartDate { get; init; }

                                  [GreaterThanProperty("NonExistent")]
                                  public DateTime EndDate { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeTrue(
            "referencing non-existent property in GreaterThanProperty should emit PRAG0203");
    }

    /// <summary>
    ///     ⚠️ <c>[RequiredIf]</c> wrote its outer condition before checking the name: PRAG0203 came
    ///     with a CS0103 from inside a generated file, on an identifier the author never typed.
    ///     One defect, one error.
    /// </summary>
    [Theory]
    [InlineData("RequiredIf")]
    [InlineData("RequiredIfNot")]
    public void ComparisonPropertyNotFound_ConditionalOnNonExistent_EmitsPrag0203Alone(string attribute)
    {
        var source = $$"""
                       using Pragmatic.Validation.Attributes;

                       namespace TestNamespace;

                       public partial record PaymentRequest
                       {
                           public bool UseCard { get; init; }

                           [{{attribute}}("Nonexistent", true)]
                           public string? CardNumber { get; init; }
                       }
                       """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeTrue(
            $"[{attribute}] naming a property that does not exist should emit PRAG0203");
        HasCompilationErrors(result).Should().BeFalse(
            "the diagnostic is the only error: "
            + string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        GetGeneratedSource(result, "PaymentRequest.Validator")!
            .Should().NotContain("Nonexistent", "a rule on a name that does not exist is not written");
    }

    /// <summary>And the control: the same rule on a name that exists is written.</summary>
    [Theory]
    [InlineData("RequiredIf")]
    [InlineData("RequiredIfNot")]
    public void ComparisonPropertyNotFound_ConditionalOnExisting_IsRendered(string attribute)
    {
        var source = $$"""
                       using Pragmatic.Validation.Attributes;

                       namespace TestNamespace;

                       public partial record PaymentRequest
                       {
                           public bool UseCard { get; init; }

                           [{{attribute}}(nameof(UseCard), true)]
                           public string? CardNumber { get; init; }
                       }
                       """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0203").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "PaymentRequest.Validator")!
            .Should().Contain("Equals(UseCard, true)");
    }

    #endregion

    #region PRAG0220: ValidEnumOnNonEnum

    /// <summary>
    ///     <c>[ValidEnum]</c> on something that is not an enum: PRAG0220, and the rule is not written.
    /// </summary>
    /// <remarks>
    ///     The rule compiles to <c>Enum.IsDefined&lt;T&gt;</c>, whose constraint a string cannot meet:
    ///     written anyway it was a CS0453 from inside a generated file. The diagnostic replaces the
    ///     compiler error rather than accompanying it.
    /// </remarks>
    [Fact]
    public void ValidEnumOnNonEnum_String_EmitsPrag0220AndSkipsTheRule()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record PaintRequest
                              {
                                  [ValidEnum]
                                  public string Shade { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0220").Should().BeTrue(
            "[ValidEnum] on a string should emit PRAG0220");
        GetGeneratorDiagnostics(result).Should().Contain(d =>
            d.Id == "PRAG0220" && d.GetMessage().Contains("Shade"));
        HasCompilationErrors(result).Should().BeFalse(
            "the diagnostic is the only error: "
            + string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
        GetGeneratedSource(result, "PaintRequest.Validator")!
            .Should().NotContain("IsValidEnum", "a rule that cannot compile is not written");
    }

    /// <summary>The control: on an enum the rule is written and nothing is reported.</summary>
    [Fact]
    public void ValidEnumOnNonEnum_Enum_NoDiagnostic()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public enum Shade { Light = 1, Dark = 2 }

                              public partial record PaintRequest
                              {
                                  [ValidEnum]
                                  public Shade Tint { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0220").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse();
        GetGeneratedSource(result, "PaintRequest.Validator")!
            .Should().Contain("IsValidEnum(Tint)");
    }

    #endregion

    #region PRAG0204: ValidateElementsOnNonCollection

    // NOTE: [ValidateElements] is AUTO-ENABLED for collections with validatable element types.
    // The attribute is only needed for configuration (e.g., StopOnFirstError).
    // These diagnostics would warn users who explicitly use [ValidateElements] on wrong types.

    /// <summary>
    ///     Verifies PRAG0204 is emitted when [ValidateElements] is used explicitly on a non-collection property.
    /// </summary>
    [Fact]
    public void ValidateElementsOnNonCollection_SingleObject_EmitsPrag0204()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AddressDto
                              {
                                  [Required]
                                  public string Street { get; init; } = "";
                              }

                              public partial record CustomerRequest
                              {
                                  [ValidateElements]
                                  public AddressDto? Address { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0204").Should().BeTrue(
            "[ValidateElements] on non-collection should emit PRAG0204");

        var diagnostics = GetGeneratorDiagnostics(result).ToList();
        diagnostics.Should().Contain(d =>
            d.Id == "PRAG0204" &&
            d.GetMessage().Contains("Address"));
    }

    /// <summary>
    ///     Verifies PRAG0204 is emitted when [ValidateElements] is used on a primitive type.
    /// </summary>
    [Fact]
    public void ValidateElementsOnNonCollection_PrimitiveType_EmitsPrag0204()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record BadRequest
                              {
                                  [ValidateElements]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0204").Should().BeTrue(
            "[ValidateElements] on string should emit PRAG0204");
    }

    /// <summary>
    ///     Verifies collection validation is AUTO-ENABLED without [ValidateElements] attribute.
    /// </summary>
    [Fact]
    public void ValidateElements_AutoEnabled_ForValidatableCollections()
    {
        // NOTE: No [ValidateElements] attribute - validation should be automatic!
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ItemDto
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }

                              public partial record OrderRequest
                              {
                                  [Required]
                                  public string OrderNumber { get; init; } = "";

                                  // No [ValidateElements] - should auto-validate!
                                  public List<ItemDto> Items { get; init; } = new();
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "OrderRequest.Validator");
        generated.Should().NotBeNull();
        // Verify auto-validation of collection elements is generated
        generated.Should().Contain("Items[i].Validate()",
            "collection elements should be auto-validated without explicit [ValidateElements]");
    }

    /// <summary>
    ///     Verifies no PRAG0204 when [ValidateElements] is used on a List.
    /// </summary>
    [Fact]
    public void ValidateElementsOnNonCollection_List_NoDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record AddressDto
                              {
                                  [Required]
                                  public string Street { get; init; } = "";
                              }

                              public partial record CustomerRequest
                              {
                                  [ValidateElements]
                                  public List<AddressDto> Addresses { get; init; } = new();
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0204").Should().BeFalse(
            "[ValidateElements] on List should not emit PRAG0204");
    }

    /// <summary>
    ///     Verifies no PRAG0204 when [ValidateElements] is used on an array.
    /// </summary>
    [Fact]
    public void ValidateElementsOnNonCollection_Array_NoDiagnostic()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ItemDto
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }

                              public partial record OrderRequest
                              {
                                  [ValidateElements]
                                  public ItemDto[] Items { get; init; } = [];
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0204").Should().BeFalse(
            "[ValidateElements] on array should not emit PRAG0204");
    }

    #endregion

    #region PRAG0205: ValidateElementsNotValidatable

    /// <summary>
    ///     Verifies PRAG0205 is emitted when element type doesn't have validation attributes.
    /// </summary>
    [Fact]
    public void ValidateElementsNotValidatable_NoAttributes_EmitsPrag0205()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              // No validation attributes - not validatable
                              public class PlainDto
                              {
                                  public string Name { get; set; } = "";
                              }

                              public partial record ContainerRequest
                              {
                                  [ValidateElements]
                                  public List<PlainDto> Items { get; init; } = new();
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0205").Should().BeTrue(
            "element type without validation should emit PRAG0205");

        var diagnostics = GetGeneratorDiagnostics(result).ToList();
        diagnostics.Should().Contain(d =>
            d.Id == "PRAG0205" &&
            d.GetMessage().Contains("PlainDto"));
    }

    /// <summary>
    ///     Verifies no PRAG0205 when element type implements ISyncValidator.
    /// </summary>
    [Fact]
    public void ValidateElementsNotValidatable_WithValidation_NoDiagnostic()
    {
        const string source = """
                              using System.Collections.Generic;
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record ItemDto
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }

                              public partial record ContainerRequest
                              {
                                  [ValidateElements]
                                  public List<ItemDto> Items { get; init; } = new();
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0205").Should().BeFalse(
            "validatable element type should not emit PRAG0205");
    }

    #endregion

    #region PRAG0209: IncompatibleComparisonTypes

    /// <summary>
    ///     Verifies PRAG0209 is emitted when comparison properties have incompatible types.
    /// </summary>
    [Fact]
    public void IncompatibleComparisonTypes_StringVsInt_EmitsPrag0209()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record MismatchedRequest
                              {
                                  public int Count { get; init; }

                                  [EqualTo(nameof(Count))]
                                  public string CountString { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0209").Should().BeTrue(
            "comparing string with int should emit PRAG0209");

        var diagnostics = GetGeneratorDiagnostics(result).ToList();
        diagnostics.Should().Contain(d =>
            d.Id == "PRAG0209" &&
            d.GetMessage().Contains("CountString") &&
            d.GetMessage().Contains("Count"));
    }

    /// <summary>
    ///     Verifies no PRAG0209 when comparison properties have compatible types.
    /// </summary>
    [Fact]
    public void IncompatibleComparisonTypes_MatchingTypes_NoDiagnostic()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record PasswordRequest
                              {
                                  public string Password { get; init; } = "";

                                  [EqualTo(nameof(Password))]
                                  public string ConfirmPassword { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0209").Should().BeFalse(
            "matching types should not emit PRAG0209");
        HasCompilationErrors(result).Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    /// <summary>
    ///     Verifies generator handles empty class gracefully.
    /// </summary>
    [Fact]
    public void EdgeCase_EmptyClass_NoValidationGenerated()
    {
        const string source = """
                              namespace TestNamespace;

                              public partial record EmptyRequest
                              {
                              }
                              """;

        var result = RunGenerator(source);

        // Should not emit any Validation-specific files for empty class
        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().NotContain(k => k.Contains("EmptyRequest") && k.Contains("Validator"));
    }

    /// <summary>
    ///     Verifies generator handles nested namespace correctly.
    /// </summary>
    [Fact]
    public void EdgeCase_NestedNamespace_GeneratesCorrectly()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace Company.Product.Module.Requests;

                              public partial record DeepRequest
                              {
                                  [Required]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var sources = GetAllGeneratedSources(result);
        sources.Keys.Should().Contain(k =>
            k.Contains("DeepRequest.Validator"));
    }

    /// <summary>
    ///     Verifies generator handles multiple validation attributes on same property.
    /// </summary>
    [Fact]
    public void EdgeCase_MultipleAttributesSameProperty_AllValidated()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record StrictRequest
                              {
                                  [Required]
                                  [MinLength(5)]
                                  [MaxLength(100)]
                                  [Email]
                                  public string Email { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "StrictRequest.Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("IsNullOrEmpty");      // Required
        generated.Should().Contain("Length < 5");          // MinLength
        generated.Should().Contain("Length > 100");        // MaxLength
        generated.Should().Contain("IsValidEmail");        // Email
    }

    /// <summary>
    ///     Verifies generator handles nullable reference types correctly.
    /// </summary>
    [Fact]
    public void EdgeCase_NullableReferenceType_ValidatesCorrectly()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record NullableRequest
                              {
                                  [Required]
                                  public string? OptionalName { get; init; }

                                  [MinLength(1)]
                                  public string? OptionalDescription { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "NullableRequest.Validator");
        generated.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies generator handles generic class with constraints.
    /// </summary>
    [Fact]
    public void EdgeCase_GenericClassWithConstraints_HandledCorrectly()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record GenericRequest<T> where T : class
                              {
                                  [Required]
                                  public T Value { get; init; } = default!;

                                  [Required]
                                  public string Name { get; init; } = "";
                              }
                              """;

        var result = RunGenerator(source);

        // Generic types may or may not be supported - verify no crashes
        GetGeneratorDiagnostics(result)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty("generator should not crash on generic types");
    }

    /// <summary>
    ///     Verifies generator handles struct types.
    /// </summary>
    [Fact]
    public void EdgeCase_StructType_GeneratesCorrectly()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial struct CoordinateRequest
                              {
                                  [Range(-90, 90)]
                                  public double Latitude { get; init; }

                                  [Range(-180, 180)]
                                  public double Longitude { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var generated = GetGeneratedSource(result, "CoordinateRequest.Validator");
        generated.Should().NotBeNull();
        generated.Should().Contain("partial struct CoordinateRequest");
    }

    #endregion
}
