using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for ValidateElements attribute code generation.
/// </summary>
public class ValidateElementsGeneratorTests : ValidationGeneratorTestBase
{
    [Fact]
    public void Generator_WithValidateElements_GeneratesElementValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record OrderItemRequest
                              {
                                  [Required]
                                  public string ProductId { get; init; }

                                  [Range(1, 100)]
                                  public int Quantity { get; init; }
                              }

                              public partial record PlaceOrderRequest
                              {
                                  [Required]
                                  [MinCount(1)]
                                  public List<OrderItemRequest> Items { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        // Should generate validator for PlaceOrderRequest
        var generatedSource = GetGeneratedSource(result, "PlaceOrderRequest.Validator");
        generatedSource.Should().NotBeNull();

        // Should include loop over Items
        generatedSource.Should().Contain("Items");

        // Should generate validator for OrderItemRequest
        var itemValidator = GetGeneratedSource(result, "OrderItemRequest.Validator");
        itemValidator.Should().NotBeNull();
        itemValidator.Should().Contain("ProductId");
        itemValidator.Should().Contain("Quantity");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidateElements_GeneratesIndexedErrorPaths()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record LineItem
                              {
                                  [Required]
                                  public string Sku { get; init; }
                              }

                              public partial record OrderRequest
                              {
                                  // Bare, and legal: this is the only annotation OrderRequest has, so it
                                  // is what puts the type in the pipeline. PRAG0223 accuses the bare form
                                  // only where the type has other rules.
                                  [ValidateElements]
                                  public List<LineItem> Lines { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "OrderRequest.Validator");
        generatedSource.Should().NotBeNull();

        // Should generate indexed paths like "Lines[i].Sku"
        generatedSource.Should().Contain("Lines[");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidateElements_Array_GeneratesValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;

                              namespace TestNamespace;

                              public partial record Tag
                              {
                                  [Required]
                                  [MinLength(2)]
                                  public string Name { get; init; }
                              }

                              public partial record ArticleRequest
                              {
                                  [ValidateElements]
                                  public Tag[] Tags { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "ArticleRequest.Validator");
        generatedSource.Should().NotBeNull();

        // Should handle array type
        generatedSource.Should().Contain("Tags");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithValidateElementsStopOnFirstError_GeneratesBreakLogic()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record Item
                              {
                                  [Required]
                                  public string Id { get; init; }
                              }

                              public partial record BatchRequest
                              {
                                  [ValidateElements(StopOnFirstError = true)]
                                  public List<Item> Items { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "BatchRequest.Validator");
        generatedSource.Should().NotBeNull();

        // Should generate break logic for StopOnFirstError
        generatedSource.Should().Contain("break");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithMultipleValidateElements_GeneratesAllValidation()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record Address
                              {
                                  [Required]
                                  public string Street { get; init; }
                              }

                              public partial record Phone
                              {
                                  [Required]
                                  public string Number { get; init; }
                              }

                              public partial record ContactRequest
                              {
                                  [ValidateElements]
                                  public List<Address> Addresses { get; init; }

                                  [ValidateElements]
                                  public List<Phone> Phones { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "ContactRequest.Validator");
        generatedSource.Should().NotBeNull();
        generatedSource.Should().Contain("Addresses");
        generatedSource.Should().Contain("Phones");

        // Should generate validators for both element types
        GetGeneratedSource(result, "Address.Validator").Should().NotBeNull();
        GetGeneratedSource(result, "Phone.Validator").Should().NotBeNull();

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }

    [Fact]
    public void Generator_WithNullableCollection_GeneratesNullCheck()
    {
        const string source = """
                              using Pragmatic.Validation.Attributes;
                              using System.Collections.Generic;

                              namespace TestNamespace;

                              public partial record LineItem
                              {
                                  [Required]
                                  public string Sku { get; init; }
                              }

                              public partial record OrderRequest
                              {
                                  [ValidateElements]
                                  public List<LineItem>? OptionalItems { get; init; }
                              }
                              """;

        var result = RunGenerator(source);

        var generatedSource = GetGeneratedSource(result, "OrderRequest.Validator");
        generatedSource.Should().NotBeNull();

        // Should generate null check for nullable collection
        generatedSource.Should().Contain("OptionalItems");

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.GetMessage())));
    }
}