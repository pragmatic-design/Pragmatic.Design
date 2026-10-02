// Tests for change-aware entity validation generation.
// Entities with [Entity] attribute get Validate(IReadOnlySet<string>?) generated.

using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.Validation.Tests.Generator;

/// <summary>
///     Tests for entity change-aware validation generation.
/// </summary>
public class EntityValidationGeneratorTests : ValidationGeneratorTestBase
{
    private static readonly MetadataReference[] PersistenceReferences =
    [
        GeneratorTestHelper.FromType<EntityAttribute>()
    ];

    private static SourceGenRunResult RunEntityGenerator(string source)
        => RunGenerator(source, PersistenceReferences);

    [Fact]
    public void Entity_WithValidationAttributes_GeneratesChangeAwareValidate()
    {
        var source = """
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class Product : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Name { get; private set; } = "";

                [Range(0, 9999)]
                public decimal Price { get; private set; }
            }
            """;

        var result = RunEntityGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Product.Validator");
        generated.Should().NotBeNull();

        // Should have change-aware overload
        generated.Should().Contain("Validate(IReadOnlySet<string>? modifiedProperties)");
        generated.Should().Contain("propsToValidate");
        generated.Should().Contain("nameof(Name)");
        generated.Should().Contain("nameof(Price)");

        // Parameterless Validate() should delegate to overload
        generated.Should().Contain("Validate(modifiedProperties: null)");
    }

    [Fact]
    public void Entity_WithCrossPropertyRules_GeneratesExpandDependencies()
    {
        var source = """
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class Booking : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [FutureDate]
                public DateTimeOffset CheckIn { get; private set; }

                [GreaterThanProperty(nameof(CheckIn))]
                public DateTimeOffset CheckOut { get; private set; }
            }
            """;

        var result = RunEntityGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Booking.Validator");
        generated.Should().NotBeNull();

        // Should generate ExpandDependencies
        generated.Should().Contain("ExpandDependencies");
        generated.Should().Contain("nameof(CheckIn)");
        generated.Should().Contain("nameof(CheckOut)");
    }

    [Fact]
    public void Dto_WithValidationAttributes_DoesNotGenerateChangeAwareValidate()
    {
        var source = """
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            public partial class CreateProductRequest
            {
                [Required]
                public string Name { get; set; } = "";

                [Range(0, 9999)]
                public decimal Price { get; set; }
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "CreateProductRequest.Validator");
        generated.Should().NotBeNull();

        // Should NOT have change-aware overload
        generated.Should().NotContain("IReadOnlySet<string>?");
        generated.Should().NotContain("propsToValidate");
        generated.Should().NotContain("ExpandDependencies");
    }

    [Fact]
    public void Entity_WithRequiredGuid_GeneratesGuidEmptyCheck()
    {
        var source = """
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class Order : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public Guid CustomerId { get; private set; }
            }
            """;

        var result = RunEntityGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "Order.Validator");
        generated.Should().NotBeNull();

        // Should check for Guid.Empty, not "CustomerId is null"
        generated.Should().Contain("Guid.Empty");
        generated.Should().NotContain("CustomerId is null");
    }

    [Fact]
    public void Entity_WithNavigationCollection_DoesNotAutoValidateElements()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class Parent : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Name { get; private set; } = "";

                public ICollection<Child> Children { get; set; } = [];
            }

            [Entity]
            public partial class Child : IEntity, ISyncValidator
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Value { get; private set; } = "";

                public ValidationError Validate() => ValidationError.Valid;
                public ValidationError Validate(IReadOnlySet<string>? modifiedProperties) => Validate();
            }
            """;

        var result = RunEntityGenerator(source);

        var generated = GetGeneratedSource(result, "Parent.Validator");
        generated.Should().NotBeNull();

        // Should NOT auto-validate navigation collection elements
        generated.Should().NotContain("Children");
        generated.Should().NotContain("ValidateElements");
    }

    [Fact]
    public void Entity_WithNoDependencies_DoesNotGenerateExpandDependencies()
    {
        var source = """
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Validation.Attributes;

            namespace TestNs;

            [Entity]
            public partial class SimpleEntity : IEntity
            {
                public Guid PersistenceId { get; set; }
                public Guid Id => PersistenceId;

                [Required]
                public string Name { get; private set; } = "";

                [Range(1, 5)]
                public int Rating { get; private set; }
            }
            """;

        var result = RunEntityGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(e => e.ToString())));

        var generated = GetGeneratedSource(result, "SimpleEntity.Validator");
        generated.Should().NotBeNull();

        // Has change-aware but no ExpandDependencies (no cross-property rules)
        generated.Should().Contain("Validate(IReadOnlySet<string>? modifiedProperties)");
        generated.Should().NotContain("ExpandDependencies");
    }
}
