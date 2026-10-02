// Tests for nested object validation and 'required' modifier detection.

using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for nested validatable object generation and 'required' modifier implicit validation.
/// </summary>
public class NestedValidationGeneratorTests : ValidationGeneratorTestBase
{
    private static readonly MetadataReference[] PersistenceReferences =
    [
        GeneratorTestHelper.FromType<EntityAttribute>()
    ];

    #region Nested Object Validation

    [Fact]
    public void NestedValidatableObject_GeneratesRecursiveValidation()
    {
        // Address has validation attributes → SG generates Validate() for both types
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Address
            {
                [Required]
                public string Street { get; init; } = "";

                [Required]
                public string City { get; init; } = "";
            }

            public partial class CreateOrderRequest
            {
                [Required]
                public string Name { get; init; } = "";

                public Address? ShippingAddress { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "CreateOrderRequest.Validator");
        generated.Should().NotBeNull();

        // Should validate nested object
        generated.Should().Contain("ShippingAddress.Validate()");
        generated.Should().Contain("nestedError");
        generated.Should().Contain("WithNested(nameof(ShippingAddress)");
    }

    [Fact]
    public void NestedNullableObject_GeneratesNullGuard()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Address
            {
                [Required]
                public string Street { get; init; } = "";
            }

            public partial class Request
            {
                [Required]
                public string Name { get; init; } = "";

                public Address? BillingAddress { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // Nullable nested object should have null guard
        generated.Should().Contain("BillingAddress is not null");
        generated.Should().Contain("BillingAddress.Validate()");
    }

    [Fact]
    public void NonValidatableObject_DoesNotGenerateNestedValidation()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public class SimpleDto
            {
                public string Value { get; init; } = "";
            }

            public partial class Request
            {
                [Required]
                public string Name { get; init; } = "";

                public SimpleDto? Data { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // SimpleDto doesn't implement ISyncValidator — no nested validation
        generated.Should().NotContain("Data");
        generated.Should().NotContain("nestedError");
    }

    [Fact]
    public void EntityWithNavigationProperty_SkipsNestedValidation()
    {
        // Child is an entity with validation attrs — SG generates Validate() for Child too.
        // Parent is an entity — nested entity properties (nav props) are skipped.
        var source = """
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class Child : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Name { get; private set; } = "";
            }

            [Entity]
            public partial class Parent : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Title { get; private set; } = "";

                public Child? MainChild { get; private set; }
            }
            """;

        var result = RunGenerator(source, PersistenceReferences);

        var generated = GetGeneratedSource(result, "Parent.Validator");
        generated.Should().NotBeNull();

        // Entity nav properties validate independently — no nested validation
        generated.Should().NotContain("MainChild");
        generated.Should().NotContain("nestedError");
    }

    [Fact]
    public void NestedWithRequiredAttribute_ValidatesBothRequiredAndNested()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Address
            {
                [Required]
                public string Street { get; init; } = "";
            }

            public partial class Request
            {
                [Required]
                public Address? Address { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // Should have Required check AND nested validation in else block
        generated.Should().Contain("Address is null");
        generated.Should().Contain("Address.Validate()");
        generated.Should().Contain("nestedError");
    }

    [Fact]
    public void NestedObjectWithValidationAttributes_DetectedViaHeuristic()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Address
            {
                [Required]
                public string Street { get; init; } = "";

                [Required]
                public string City { get; init; } = "";
            }

            public partial class Request
            {
                [Required]
                public string Name { get; init; } = "";

                public Address? ShippingAddress { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // Address is detected as validatable via heuristic (has validation attributes)
        generated.Should().Contain("ShippingAddress.Validate()");
    }

    [Fact]
    public void CollectionWithNestedValidatableElements_GeneratesBothPaths()
    {
        var source = """
            using System.Collections.Generic;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class LineItem
            {
                [Required]
                public string ProductId { get; init; } = "";
            }

            public partial class Order
            {
                [Required]
                public string CustomerId { get; init; } = "";

                [MinCount(1)]
                public List<LineItem> Items { get; init; } = [];
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Order.Validator");
        generated.Should().NotBeNull();

        // Collection element validation uses indexed WithNested
        generated.Should().Contain("Items[i]");
        generated.Should().Contain("WithNested(nameof(Items), i,");
    }

    #endregion

    #region Required Modifier Detection

    [Fact]
    public void RequiredModifier_OnString_GeneratesImplicitRequiredCheck()
    {
        var source = """
            namespace TestNs;

            public partial class Request
            {
                public required string Name { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // 'required string' → implicit Required → string.IsNullOrEmpty check
        generated.Should().Contain("string.IsNullOrEmpty(Name)");
        generated.Should().Contain("\"validation.required\"");
    }

    [Fact]
    public void RequiredModifier_WithExplicitRequired_NoDuplication()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Request
            {
                [Required]
                public required string Name { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // Should have only ONE Required check, not duplicated
        var requiredCount = generated!.Split("\"validation.required\"").Length - 1;
        requiredCount.Should().Be(1, "explicit [Required] should not be duplicated by 'required' modifier");
    }

    [Fact]
    public void RequiredModifier_OnNonNullableValueType_SkipsImplicitRequired()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class Request
            {
                [Required]
                public required string Name { get; init; }

                [Positive]
                public required int Quantity { get; init; }

                [Range(0.01, 99999.99)]
                public required decimal Price { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // Non-nullable value types with 'required' don't need null checks — compiler enforces init
        generated.Should().NotContain("Quantity is null");
        generated.Should().NotContain("Price is null");

        // But validation attributes should still apply
        generated.Should().Contain("Quantity <= 0");
        generated.Should().Contain("Price <");
    }

    [Fact]
    public void RequiredModifier_OnGuid_GeneratesEmptyCheck()
    {
        var source = """
            using System;

            namespace TestNs;

            public partial class Request
            {
                public required Guid OrderId { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // 'required Guid' → implicit Required → Guid.Empty check
        generated.Should().Contain("Guid.Empty");
    }

    [Fact]
    public void RequiredModifier_OnNullableReferenceType_GeneratesNullCheck()
    {
        var source = """
            namespace TestNs;

            public partial class Request
            {
                public required string? Description { get; init; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Request.Validator");
        generated.Should().NotBeNull();

        // 'required string?' → implicit Required → string.IsNullOrEmpty check (IsString=true)
        generated.Should().Contain("string.IsNullOrEmpty(Description)");
        generated.Should().Contain("\"validation.required\"");
    }

    #endregion
}
