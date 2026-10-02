using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for diagnostic emission by the mapping source generator.
///     These tests verify that appropriate errors and warnings are emitted
///     when the generator encounters invalid or problematic configurations.
/// </summary>
public class DiagnosticTests : MappingGeneratorTestBase
{
    // =========================================================================
    // PRAG0300: Type must be partial
    // =========================================================================

    [Fact]
    public void NonPartialClass_SkipsGeneration()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         // Missing 'partial' keyword
                         [MapFrom<Source>]
                         public record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Generator should skip non-partial types (no output generated)
        HasNoGeneratedFiles(result).Should().BeTrue(
            "non-partial classes cannot have generated partial implementations");

        // PRAG0300 is the companion analyzer's, on the declaration
        // (AMustBePartialDiagnosticHasOneOwnerTests): the generator skips without a second copy.
        HasDiagnostic(result, "PRAG0300").Should().BeFalse("one ID, one owner: the analyzer reports it");
    }

    [Fact]
    public void PartialClass_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasNoGeneratedFiles(result).Should().BeFalse(
            "partial classes should generate mapping code");
        HasCompilationErrors(result).Should().BeFalse();
    }

    // =========================================================================
    // PRAG0303: No matching source property (Warning)
    // =========================================================================

    [Fact]
    public void UnmatchedProperty_GeneratesWithDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                             // Note: no 'Name' property
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";  // No matching source
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Generator should still generate code for matched properties
        HasNoGeneratedFiles(result).Should().BeFalse();

        // The unmatched property should get a default comment
        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // Unmatched properties should not cause compilation errors
        // (they just won't be mapped)
        HasCompilationErrors(result).Should().BeFalse();

        // Should emit PRAG0303 warning for unmapped 'Name' property
        HasDiagnostic(result, "PRAG0303").Should().BeTrue(
            "unmapped property should emit PRAG0303 warning");
    }

    // =========================================================================
    // PRAG0302: Property not found (MapProperty with invalid path)
    // =========================================================================

    [Fact]
    public void MapProperty_WithInvalidPath_CompilesButUnmapped()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }

                             [MapProperty("NonExistent.Path")]
                             public string Value { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Should generate but the property mapping might be incomplete
        HasNoGeneratedFiles(result).Should().BeFalse();

        // The generated code should compile (with default value)
        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // The explicit invalid path is a clear PRAG0302 (not the generic 0303, which is suppressed here).
        HasDiagnostic(result, "PRAG0302").Should().BeTrue(
            "an explicit [MapProperty] path that doesn't resolve must emit PRAG0302");
        HasCompilationErrors(result).Should().BeFalse();
    }

    // =========================================================================
    // PRAG0310: [GenerateProjection] requires [MapFrom]
    // =========================================================================

    [Fact]
    public void GenerateProjection_WithoutMapFrom_SkipsProjection()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         // Has [GenerateProjection] but no [MapFrom]
                         [GenerateProjection]
                         public partial record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Without [MapFrom], the generator shouldn't generate anything
        // (or should generate without projection)
        var mainSource = GetGeneratedSource(result, "Target.Mapping");

        // The projection should not be generated without [MapFrom]
        if (mainSource is not null)
            mainSource.Should().NotContain("Projection { get; }");

        // And the misuse must be surfaced, not silent.
        HasDiagnostic(result, "PRAG0310").Should().BeTrue(
            "[GenerateProjection] without [MapFrom] must emit PRAG0310");
    }

    [Fact]
    public void GenerateProjection_WithMapFrom_GeneratesProjection()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<Source>]
                         [GenerateProjection]
                         public partial record Target
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();
        mainSource.Should().Contain("Projection { get; }");
        mainSource.Should().Contain("Expression<Func<");
    }

    // =========================================================================
    // PRAG0314: Conflicting attributes ([MapIgnore] and [MapProperty])
    // =========================================================================

    [Fact]
    public void MapIgnore_TakesPrecedence_OverMapProperty()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                             public string Secret { get; set; } = "";
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }

                             // Conflicting attributes - MapIgnore should win
                             [MapIgnore]
                             [MapProperty("Secret")]
                             public string Hidden { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // The property should be ignored (not in context struct)
        mainSource.Should().NotContain("Hidden = entity.Secret");

        // The contradiction is surfaced (PRAG0314), not silently swallowed.
        HasDiagnostic(result, "PRAG0314").Should().BeTrue(
            "[MapIgnore] + [MapProperty] on the same property must emit PRAG0314");
    }

    // =========================================================================
    // PRAG0304: Incompatible simple types
    // =========================================================================

    [Fact]
    public void IncompatibleSimpleTypes_ReportsPRAG0304()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public Guid Marker { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public bool Marker { get; init; }  // Guid → bool: no conversion exists
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0304").Should().BeTrue(
            "Guid → bool has no conversion path and must be a clear PRAG error");
    }

    [Fact]
    public void CompatibleWidening_DoesNotReportPRAG0304()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Count { get; set; }
                             public System.DateTime When { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public long Count { get; init; }                    // numeric widening
                             public System.DateTimeOffset When { get; init; }    // implicit BCL conversion
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0304").Should().BeFalse(
            "compiler-provided implicit conversions must not be flagged");
        HasCompilationErrors(result).Should().BeFalse();
    }

    [Fact]
    public void FormatString_ProducesString_DoesNotReportPRAG0304()
    {
        // Regression (found on Showcase.Catalog): [MapProperty(Format=...)] turns the expression into
        // .ToString("...") → string, so DateTimeOffset → string is NOT incompatible.
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public System.DateTimeOffset CreatedAt { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             [MapProperty("CreatedAt", Format = "yyyy-MM-dd")]
                             public string CreatedDate { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0304").Should().BeFalse("Format produces a string expression");
        HasCompilationErrors(result).Should().BeFalse();
    }

    // =========================================================================
    // PRAG0305/PRAG0306: [MapConverter] contract violations
    // =========================================================================

    [Fact]
    public void Converter_WithoutInterface_ReportsPRAG0305()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class NotAConverter { }  // does NOT implement IValueConverter<,>

                         public class Source { public string Value { get; set; } = ""; }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             [MapConverter<NotAConverter>]
                             public string Value { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0305").Should().BeTrue(
            "a converter that doesn't implement IValueConverter<,> must emit PRAG0305");
    }

    [Fact]
    public void Converter_WithoutParameterlessCtor_ReportsPRAG0306()
    {
        var source = """
                     using Pragmatic.Mapping.Converters;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class NeedsArgsConverter : IValueConverter<string, string>
                         {
                             public NeedsArgsConverter(string prefix) { }
                             public string Convert(string source) => source;
                             public string ConvertBack(string target) => target;
                         }

                         public class Source { public string Value { get; set; } = ""; }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             [MapConverter<NeedsArgsConverter>]
                             public string Value { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0306").Should().BeTrue(
            "the generated code instantiates converters with new(): a missing parameterless ctor must emit PRAG0306");
    }

    // =========================================================================
    // PRAG0315: Nested DTO [MapFrom<T>] unrelated to the navigation type
    // =========================================================================

    [Fact]
    public void NestedDto_WithUnrelatedSource_ReportsPRAG0315()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Supplier { public string Name { get; set; } = ""; }
                         public class Customer { public string Name { get; set; } = ""; }

                         public class Order
                         {
                             public Customer Customer { get; set; } = new();
                         }

                         // Declared over Supplier, but Order.Customer is a Customer.
                         [MapFrom<Supplier>]
                         public partial record CustomerDto
                         {
                             public string Name { get; init; } = "";
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public CustomerDto Customer { get; init; } = null!;
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0315").Should().BeTrue(
            "a nested DTO whose [MapFrom<T>] is unrelated to the navigation type must emit PRAG0315");
    }

    // =========================================================================
    // PRAG0317: Nullable → non-nullable without Default (no auto-default available)
    // =========================================================================

    [Fact]
    public void NullableCustomType_ToNonNullable_ReportsPRAG0317()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public struct Money { public decimal Amount { get; set; } }

                         public class Source
                         {
                             public Money? Price { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public Money Price { get; init; }  // no Default, no auto-default for custom struct
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0317").Should().BeTrue(
            "nullable → non-nullable of a custom type has no auto-default and must emit PRAG0317");
    }

    // =========================================================================
    // PRAG0323: Direct match AND flattening convention both apply
    // =========================================================================

    [Fact]
    public void DirectMatchAndFlattening_BothApply_ReportsPRAG0323()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Customer { public string Name { get; set; } = ""; }

                         public class Order
                         {
                             public string CustomerName { get; set; } = "";   // direct match
                             public Customer Customer { get; set; } = new();  // flattening also matches
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public string CustomerName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0323").Should().BeTrue(
            "a property matched by BOTH direct name and flattening must warn (direct wins)");

        // Direct match must still win.
        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain("entity.CustomerName");
    }

    [Fact]
    public void ForeignKeyConvention_IsNotAmbiguous_NoPRAG0323()
    {
        // Regression (found on Showcase.Booking): the EF FK pattern ({Nav}Id property + {Nav}.Id)
        // resolves to the same value by design — flagging it would mark every foreign key.
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Property { public System.Guid Id { get; set; } }

                         public class Reservation
                         {
                             public System.Guid PropertyId { get; set; }          // FK
                             public Property Property { get; set; } = new();     // navigation
                         }

                         [MapFrom<Reservation>]
                         public partial record ReservationDto
                         {
                             public System.Guid PropertyId { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0323").Should().BeFalse(
            "the FK convention is not a real ambiguity");
        HasCompilationErrors(result).Should().BeFalse();
    }

    // =========================================================================
    // PRAG0327: nested projection truncated by MaxDepth
    // =========================================================================

    private const string DeepNestingSource = """
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class E1 { public string Name { get; set; } = ""; public E2 Next { get; set; } = new(); }
            public class E2 { public string Name { get; set; } = ""; public E3 Next { get; set; } = new(); }
            public class E3 { public string Name { get; set; } = ""; public E4 Next { get; set; } = new(); }
            public class E4 { public string Name { get; set; } = ""; public E5 Next { get; set; } = new(); }
            public class E5 { public string Name { get; set; } = ""; public E6 Next { get; set; } = new(); }
            public class E6 { public string Name { get; set; } = ""; public E7 Next { get; set; } = new(); }
            public class E7 { public string Name { get; set; } = ""; }

            [MapFrom<E7>] public partial record D7 { public string Name { get; init; } = ""; }
            [MapFrom<E6>] public partial record D6 { public string Name { get; init; } = ""; public D7 Next { get; init; } = null!; }
            [MapFrom<E5>] public partial record D5 { public string Name { get; init; } = ""; public D6 Next { get; init; } = null!; }
            [MapFrom<E4>] public partial record D4 { public string Name { get; init; } = ""; public D5 Next { get; init; } = null!; }
            [MapFrom<E3>] public partial record D3 { public string Name { get; init; } = ""; public D4 Next { get; init; } = null!; }
            [MapFrom<E2>] public partial record D2 { public string Name { get; init; } = ""; public D3 Next { get; init; } = null!; }

            [MapFrom<E1>]
            {PROJECTION}
            public partial record D1 { public string Name { get; init; } = ""; public D2 Next { get; init; } = null!; }
        }
        """;

    [Fact]
    public void DeepNestedProjection_ExceedingDefaultMaxDepth_ReportsPRAG0327()
    {
        var result = RunGenerator(DeepNestingSource.Replace("{PROJECTION}", "[GenerateProjection]"));

        HasDiagnostic(result, "PRAG0327").Should().BeTrue(
            "truncating a nested projection at the depth cap must not be silent");
    }

    [Fact]
    public void DeepNestedProjection_WithRaisedMaxDepth_NoPRAG0327()
    {
        var result = RunGenerator(DeepNestingSource.Replace("{PROJECTION}", "[GenerateProjection(MaxDepth = 10)]"));

        HasDiagnostic(result, "PRAG0327").Should().BeFalse(
            "MaxDepth = 10 accommodates the 7-level chain");
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    // =========================================================================
    // Enum → enum (different types): by-name switch + PRAG0328 validation
    // =========================================================================

    [Fact]
    public void EnumToEnum_MatchingMembers_GeneratesByNameSwitch()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum EntityStatus { Draft, Active, Archived }
                         public enum DtoStatus { Draft, Active, Archived }

                         public class Source { public EntityStatus Status { get; set; } }

                         [MapFrom<Source>]
                         public partial record Target { public DtoStatus Status { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain("entity.Status switch")
            .And.Contain("global::TestApp.EntityStatus.Draft => global::TestApp.DtoStatus.Draft")
            .And.Contain("global::TestApp.EntityStatus.Archived => global::TestApp.DtoStatus.Archived");
    }

    [Fact]
    public void EnumToEnum_MissingTargetMember_ReportsPRAG0328()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum EntityStatus { Draft, Active, Suspended }
                         public enum DtoStatus { Draft, Active }  // 'Suspended' missing

                         public class Source { public EntityStatus Status { get; set; } }

                         [MapFrom<Source>]
                         public partial record Target { public DtoStatus Status { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0328").Should().BeTrue(
            "a source enum member with no same-named target member must be a compile-time error");
        HasCompilationErrors(result).Should().BeFalse(
            "the mapping is skipped: " + string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    // =========================================================================
    // [MapCondition] — conditional mapping + PRAG0329
    // =========================================================================

    [Fact]
    public void MapCondition_ValidPredicate_GatesTheMapping()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public string Email { get; set; } = "";
                             public bool EmailVerified { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             [MapCondition(nameof(ShouldMapEmail))]
                             public string? Email { get; init; }

                             private static bool ShouldMapEmail(Source source) => source.EmailVerified;
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain("ShouldMapEmail(entity) ? (entity.Email) : default!");
    }

    [Fact]
    public void MapCondition_MissingMethod_ReportsPRAG0329()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source { public string Email { get; set; } = ""; }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             [MapCondition("NoSuchMethod")]
                             public string? Email { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0329").Should().BeTrue(
            "a [MapCondition] pointing at a missing/invalid predicate must be a clear PRAG error");
    }

    // =========================================================================
    // [MapDerived] — polymorphic dispatch + PRAG0330
    // =========================================================================

    private const string PolymorphicSource = """
        using Pragmatic.Mapping.Attributes;

        namespace TestApp
        {
            public class Animal { public string Name { get; set; } = ""; }
            public class Dog : Animal { public bool GoodBoy { get; set; } }
            public class Cat : Animal { public int Lives { get; set; } }

            [MapFrom<Dog>]
            public partial record DogDto : AnimalDto { public bool GoodBoy { get; init; } }

            [MapFrom<Cat>]
            public partial record CatDto : AnimalDto { public int Lives { get; init; } }

            [MapFrom<Animal>]
            [MapDerived<Dog, DogDto>]
            [MapDerived<Cat, CatDto>]
            public partial record AnimalDto { public string Name { get; init; } = ""; }
        }
        """;

    [Fact]
    public void MapDerived_GeneratesPolymorphicDispatch()
    {
        var result = RunGenerator(PolymorphicSource);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "AnimalDto.Mapping");
        mainSource.Should().Contain("switch (entity)")
            .And.Contain("case global::TestApp.Dog __derived0: return global::TestApp.DogDto.FromEntity(__derived0);")
            .And.Contain("case global::TestApp.Cat __derived1: return global::TestApp.CatDto.FromEntity(__derived1);");
    }

    [Fact]
    public void MapDerived_UnrelatedDto_ReportsPRAG0330()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Animal { public string Name { get; set; } = ""; }
                         public class Dog : Animal { }

                         // NOT derived from AnimalDto → invalid pair
                         [MapFrom<Dog>]
                         public partial record UnrelatedDto { public string Name { get; init; } = ""; }

                         [MapFrom<Animal>]
                         [MapDerived<Dog, UnrelatedDto>]
                         public partial record AnimalDto { public string Name { get; init; } = ""; }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0330").Should().BeTrue(
            "a [MapDerived] DTO that doesn't inherit the base DTO must be a clear PRAG error");
    }

    // =========================================================================
    // PRAG0325 (Hidden): source property silently dropped
    // =========================================================================

    [Fact]
    public void UnmappedSourceProperty_ReportsPRAG0325()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                             public string InternalNotes { get; set; } = "";  // not on the DTO
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0325").Should().BeTrue(
            "a source property the DTO silently drops must be reported (Hidden, opt-in via editorconfig)");
    }

    // =========================================================================
    // Nested DTO without [MapFrom]
    // =========================================================================

    [Fact]
    public void NestedDto_WithMapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Parent
                         {
                             public int Id { get; set; }
                             public Child? Child { get; set; }
                         }

                         public class Child
                         {
                             public int ChildId { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<Child>]
                         public partial record ChildDto
                         {
                             public int ChildId { get; init; }
                             public string Name { get; init; } = "";
                         }

                         [MapFrom<Parent>]
                         public partial record ParentDto
                         {
                             public int Id { get; init; }
                             public ChildDto? Child { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var parentSource = GetGeneratedSource(result, "ParentDto.Mapping");
        parentSource.Should().NotBeNull();
        parentSource.Should().Contain("ChildDto.FromEntity");
    }

    // =========================================================================
    // Collection mapping with non-DTO element type
    // =========================================================================

    [Fact]
    public void Collection_WithSimpleElements_MapsDirectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public List<int> Numbers { get; set; } = [];
                             public List<string> Names { get; set; } = [];
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public List<int> Numbers { get; init; } = [];
                             public List<string> Names { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // Simple collections should use direct copy (ToList)
        mainSource.Should().Contain(".ToList()");

        // The context struct should have the collection properties
        mainSource.Should().Contain("Numbers;");
        mainSource.Should().Contain("Names;");
    }

    // =========================================================================
    // Struct source type (no null check needed)
    // =========================================================================

    [Fact]
    public void StructSource_SkipsNullCheck()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public struct Source
                         {
                             public int Id { get; set; }
                             public string Name { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // Should NOT contain null check for struct source
        mainSource.Should().NotContain("ThrowIfNull(entity)");
    }

    [Fact]
    public void ClassSource_IncludesNullCheck()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().NotBeNull();

        // Should contain null check for class source
        mainSource.Should().Contain("ThrowIfNull(entity)");
    }

    // =========================================================================
    // MapTo with ID property exclusion
    // =========================================================================

    [Fact]
    public void MapTo_ExcludesIdByDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapTo<Entity>]
                         public partial record CreateDto
                         {
                             public int Id { get; init; }  // Should be excluded by default
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var mainSource = GetGeneratedSource(result, "CreateDto.Mapping");
        mainSource.Should().NotBeNull();

        // ToEntity should not set Id
        mainSource.Should().Contain("Name = this.Name");
        // The ToEntity method should not include Id assignment
    }

    // =========================================================================
    // Internal accessibility
    // =========================================================================

    [Fact]
    public void InternalClass_GeneratesInternalExtensions()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         internal class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         internal partial record Target
                         {
                             public int Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var extensionsSource = GetGeneratedSource(result, "Target.Extensions");
        extensionsSource.Should().NotBeNull();

        // Extensions class should match accessibility
        extensionsSource.Should().Contain("internal static class TargetMappingExtensions");
    }

    // =========================================================================
    // Empty DTO (no properties to map)
    // =========================================================================

    [Fact]
    public void EmptyDto_GeneratesMinimalMapping()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int Id { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record EmptyTarget;
                     }
                     """;

        var result = RunGenerator(source);

        // Should still generate valid code
        HasNoGeneratedFiles(result).Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse();
    }

    // =========================================================================
    // Multiple [MapFrom] attributes (mapping from multiple sources)
    // Note: Currently [MapFrom] doesn't support AllowMultiple
    // =========================================================================

    [Fact]
    public void MultipleMapFrom_CausesDuplicateAttributeError()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class UserEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         public class UserModel
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<UserEntity>]
                         [MapFrom<UserModel>]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // Multiple [MapFrom] attributes cause CS0579: duplicate attribute
        HasCompilationErrors(result).Should().BeTrue(
            "[MapFrom] does not support AllowMultiple=true");

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().Contain(e => e.Id == "CS0579");
    }

    // =========================================================================
    // Bidirectional mapping ([MapFrom] + [MapTo] on same type)
    // =========================================================================

    [Fact]
    public void MapFrom_And_MapTo_OnSameType_GeneratesBothMethods()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<Entity>]
                         [MapTo<Entity>]
                         public partial record Dto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().NotBeNull();
        mainSource.Should().Contain("FromEntity");
        mainSource.Should().Contain("ToEntity");
    }
}