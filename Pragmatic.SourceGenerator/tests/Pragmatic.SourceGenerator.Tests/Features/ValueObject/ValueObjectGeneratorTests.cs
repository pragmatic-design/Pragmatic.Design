using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.ValueObject;

/// <summary>
/// Tests for the [ValueObject] generator: Create (validate-then-return) and
/// CreateUnsafe (direct construction) factory methods on a partial record.
/// The marker attribute is defined inline in its real namespace
/// (Pragmatic.Persistence.Entity) so ForAttributeWithMetadataName matches it.
/// </summary>
public class ValueObjectGeneratorTests
{
    private const string AttributeShim = """
        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void ValueObject_WithValidate_GeneratesCreateCallingValidate()
    {
        var source = AttributeShim + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email
                {
                    public string Value { get; init; }
                    public Email(string value) => Value = value;
                    private static Email Validate(string value) => new Email(value);
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Email.ValueObject.g.cs");
        generated.Should().Contain("static global::MyApp.Email Create(string value)");
        generated.Should().Contain("=> Validate(value);");
    }

    [Fact]
    public void ValueObject_WithConstructor_GeneratesCreateUnsafe()
    {
        var source = AttributeShim + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email
                {
                    public string Value { get; init; }
                    public Email(string value) => Value = value;
                    private static Email Validate(string value) => new Email(value);
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Email.ValueObject.g.cs");
        generated.Should().Contain("CreateUnsafe(string value) => new Email(value);");
    }

    [Fact]
    public void ValueObject_GeneratedCode_Compiles()
    {
        var source = AttributeShim + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email
                {
                    public string Value { get; init; }
                    public Email(string value) => Value = value;
                    private static Email Validate(string value) => new Email(value);
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();
    }

    [Fact]
    public void ValueObject_NotPartial_ReportsDiagnostic()
    {
        var source = AttributeShim + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public record Email
                {
                    public string Value { get; init; }
                    public Email(string value) => Value = value;
                    private static Email Validate(string value) => new Email(value);
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2700").Should().BeTrue();
    }

    [Fact]
    public void ValueObject_MissingValidate_ReportsDiagnostic()
    {
        var source = AttributeShim + """

            namespace MyApp
            {
                [Pragmatic.Persistence.Entity.ValueObject]
                public partial record Email
                {
                    public string Value { get; init; }
                    public Email(string value) => Value = value;
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2701").Should().BeTrue();
    }

    [Fact]
    public void NoValueObjectMarker_ProducesNoValueObjectOutput()
    {
        var source = """
            namespace MyApp
            {
                public partial record Email
                {
                    public string Value { get; init; }
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, []);

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Should().NotContain(k => k.Contains("ValueObject"));
    }
}
