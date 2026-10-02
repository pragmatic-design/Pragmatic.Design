using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for <c>[MapConstructor]</c> selection in <c>[MapTo]</c> and the
///     <c>MapProperty.Separator</c> concatenation option — features absent from the existing suite.
///     Verified against ConstructorAnalyzer and MappingTransform.CreateExplicitMapping.
/// </summary>
public class MapConstructorAndSeparatorGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // [MapConstructor] — explicit constructor selection in ToEntity
    // =========================================================================

    [Fact]
    public void MapConstructor_ForcesConstructorUsage_InToEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class User
                         {
                             public int Id { get; set; }
                             public string Email { get; set; }

                             public User() { }

                             [MapConstructor]
                             public User(string email)
                             {
                                 Email = email;
                             }
                         }

                         [MapTo<User>]
                         public partial record CreateUserDto
                         {
                             public string Email { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CreateUserDto.Mapping");
        mainSource.Should().NotBeNull();
        // ToEntity must invoke the selected constructor with the matching DTO property.
        // ⚠️ `this.Email`, not `Email`: a constructor argument goes through the same
        // expression the assignment path uses, so a parameter whose type differs from the DTO's is
        // converted instead of being a CS1503. For a property that needs no conversion that
        // expression is the property itself, qualified as it is everywhere else.
        mainSource.Should().Contain("new global::TestApp.User(this.Email)");
    }

    [Fact]
    public void MapConstructor_ExcludesCtorParamFromObjectInitializer()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public string Name { get; set; }
                             public decimal Price { get; set; }

                             public Product() { }

                             [MapConstructor]
                             public Product(string name)
                             {
                                 Name = name;
                             }
                         }

                         [MapTo<Product>]
                         public partial record CreateProductDto
                         {
                             public string Name { get; init; } = "";
                             public decimal Price { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CreateProductDto.Mapping");
        mainSource.Should().NotBeNull();
        // Name is passed via the constructor; it must not also appear in the ToEntity object
        // initializer. Initializer entries are comma-terminated ("Name = this.Name,"), whereas
        // ApplyTo legitimately emits "entity.Name = this.Name;" — assert the comma form so this
        // check targets the initializer only (the bare form would also match the valid ApplyTo statement).
        // `this.Name` — see the note in the test above.
        mainSource.Should().Contain("new global::TestApp.Product(this.Name)");
        mainSource.Should().Contain("Price = this.Price");
        mainSource.Should().NotContain("Name = this.Name,");
    }

    [Fact]
    public void MapConstructor_UnmatchedRequiredParameter_EmitsPrag0316()
    {
        // The selected constructor has a required (non-nullable, no default) parameter
        // with no matching DTO property. Generating `default` for it would silently build an
        // invalid entity, so the generator must reject it with PRAG0316 instead.
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; }
                             public string Code { get; }

                             public Order(int id, string code)
                             {
                                 Id = id;
                                 Code = code;
                             }
                         }

                         [MapTo<Order>]
                         public partial record CreateOrderDto
                         {
                             // No 'Id' property → the required ctor parameter 'id' is unmatched.
                             public string Code { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0316").Should().BeTrue(
            "an unmatched required constructor parameter must be rejected, not silently defaulted");
    }

    // =========================================================================
    // MapProperty.Separator — multi-path concatenation
    // =========================================================================

    [Fact]
    public void MapProperty_MultiplePathsDefaultSeparator_ConcatenatesWithSpace()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Person
                         {
                             public string FirstName { get; set; } = "";
                             public string LastName { get; set; } = "";
                         }

                         [MapFrom<Person>]
                         public partial record PersonDto
                         {
                             [MapProperty(nameof(Person.FirstName), nameof(Person.LastName))]
                             public string FullName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PersonDto.Mapping");
        mainSource.Should().NotBeNull();
        // Default separator is a single space.
        mainSource.Should().Contain("entity.FirstName + \" \" + entity.LastName");
    }

    [Fact]
    public void MapProperty_MultiplePathsCustomSeparator_ConcatenatesWithSeparator()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Person
                         {
                             public string FirstName { get; set; } = "";
                             public string LastName { get; set; } = "";
                         }

                         [MapFrom<Person>]
                         public partial record PersonDto
                         {
                             [MapProperty(nameof(Person.LastName), nameof(Person.FirstName), Separator = ", ")]
                             public string FullNameReversed { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PersonDto.Mapping");
        mainSource.Should().NotBeNull();
        // Custom separator is inlined between the two source expressions.
        mainSource.Should().Contain("entity.LastName + \", \" + entity.FirstName");
    }

    /// <summary>
    ///     A join that includes an enum reads the same whether it is mapped in memory or projected.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Through <c>FromEntity</c> the cell reads <c>velocity · Term</c>, because
    ///     <c>string + enum</c> calls <c>ToString()</c>. A projection that concatenated the column would
    ///     read <c>velocity · 0</c>: the column is an <c>int</c> and SQL concatenates the number. One
    ///     declaration, two answers, and the projected one <b>wrong rather than missing</b> — an empty
    ///     column is noticed the first time somebody looks at the screen, a 0 is read as data.
    /// </remarks>
    [Fact]
    public void AJoinOverAnEnum_ProjectsTheNameItMapsInMemory()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Kind { Term, Phrase }

                         public class Word
                         {
                             public string Text { get; set; } = "";
                             public Kind Kind { get; set; }
                         }

                         [MapFrom<Word>]
                         [GenerateProjection]
                         public partial record WordDto
                         {
                             [MapProperty(nameof(Word.Text), nameof(Word.Kind), Separator = " · ")]
                             public string Label { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        var projection = GetGeneratedSource(result, "WordDto.Projection")
                         ?? GetGeneratedSource(result, "WordDto.Mapping");

        projection.Should().NotBeNull(
            string.Join(" | ", GetGeneratedSourcesAsDictionary(result).Keys));

        projection!.Should().Contain("\"Term\"",
            "the projected string has to carry the member's name, which is what the in-memory "
            + "mapping produces; concatenating the column gives the number it is stored as");
    }
}
