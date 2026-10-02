using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Guards for shapes whose naive generated code does not compile (or takes a runtime-throwing /
///     double-diagnostic path).
///     Assertion-style (no snapshot) so the intent of each guard is explicit.
/// </summary>
public class CorrectnessRegressionTests : MappingGeneratorTestBase
{
    // [MapTo] nullable value-type DTO property → non-nullable value-type entity property.
    // A plain `entity.X = this.X;` (int? → int) would be CS0266 in ToEntity and ApplyTo.
    [Fact]
    public void ToEntityAndApplyTo_NullableValueToNonNullableValue_Compiles()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Person { public int Age { get; set; } public string Name { get; set; } = ""; }
            [MapTo<Person>]
            public partial class PersonDto { public int? Age { get; init; } public string Name { get; init; } = ""; }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "PersonDto.Mapping");
        mapping.Should().Contain("this.Age.GetValueOrDefault()");
    }

    // Projection flattening a nullable navigation onto a non-nullable value-type leaf.
    // `(nav == null ? null : nav.Leaf) ?? 0` would be CS0173 (no common type null/int).
    [Fact]
    public void Projection_FlattenNullableNavigationToValueLeaf_Compiles()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Address { public int ZipCode { get; set; } }
            public class Customer { public Address? HomeAddress { get; set; } }
            [MapFrom<Customer>]
            [GenerateProjection]
            public partial class CustomerDto { public int HomeAddressZipCode { get; init; } }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "CustomerDto.Mapping");
        // default folded into the null branch, not a trailing ?? on the ternary.
        mapping.Should().Contain("entity.HomeAddress == null ? 0 : entity.HomeAddress.ZipCode");
    }

    // string? → enum. An unguarded `Enum.Parse<T>(x!, …)` would throw ArgumentNullException at runtime on null.
    [Fact]
    public void StringToEnum_NullableSource_IsNullGuarded()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public enum Color { Red, Green }
            public class Src { public string? Status { get; set; } }
            [MapFrom<Src>]
            public partial class Dto { public Color Status { get; init; } }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().Contain("is { } __str");
        mapping.Should().NotContain("Status!"); // no null-forgiving on the nullable source
    }

    // Numeric narrowing (long → int) is a clear PRAG0304 rather than a bare CS0266, and the
    // generated assignment is default! so there is no second cryptic compiler error.
    [Fact]
    public void NumericNarrowing_ReportsPrag0304_NotCompilerError()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Src { public long Big { get; set; } }
            [MapFrom<Src>]
            public partial class Dto { public int Big { get; init; } }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0304").Should().BeTrue();
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    // The counterpart of narrowing: widening (int → long) stays an implicit conversion — no diagnostic.
    [Fact]
    public void NumericWidening_StaysImplicit_NoDiagnostic()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Src { public int N { get; set; } }
            [MapFrom<Src>]
            public partial class Dto { public long N { get; init; } }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0304").Should().BeFalse();
        HasCompilationErrors(result).Should().BeFalse();
    }

    // string ↔ DateTimeOffset converts, rather than reporting PRAG0304 plus a redundant CS0029.
    [Fact]
    public void StringToDateTimeOffset_Converts()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Src { public string When { get; set; } = ""; }
            [MapFrom<Src>]
            public partial class Dto { public System.DateTimeOffset When { get; init; } }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        HasDiagnostic(result, "PRAG0304").Should().BeFalse();
        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().Contain("global::System.DateTimeOffset.Parse");
    }

    // A [MapProperty(Format=...)] value containing a double-quote (legal in .NET custom format
    // strings for literal sections) injected verbatim into .ToString("...") would break the generated
    // code. It must be escaped so the output compiles.
    [Fact]
    public void Format_WithEmbeddedQuote_IsEscaped_Compiles()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Src { public System.DateTime CreatedAt { get; set; } }
            [MapFrom<Src>]
            public partial class Dto
            {
                [MapProperty("CreatedAt", Format = "yyyy\"Z\"")]
                public string Created { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().Contain("\\\"Z\\\""); // the embedded quotes are escaped in the generated literal
    }

    [Fact]
    public void DateTimeOffsetToString_Converts()
    {
        var source = """
            using Pragmatic.Mapping.Attributes;
            public class Src { public System.DateTimeOffset When { get; set; } }
            [MapFrom<Src>]
            public partial class Dto { public string When { get; init; } = ""; }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();
        var mapping = GetGeneratedSource(result, "Dto.Mapping");
        mapping.Should().Contain(".ToString(\"o\"");
    }
}
