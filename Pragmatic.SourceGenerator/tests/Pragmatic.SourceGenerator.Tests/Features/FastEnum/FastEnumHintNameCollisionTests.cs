using System.Text.Json.Serialization;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.FastEnum;

/// <summary>
/// Two [FastEnum] enums with the same simple name in different
/// namespaces must each produce a distinct AddSource hint. Roslyn requires unique hint
/// names per generator run; a collision aborts generation for the whole compilation.
/// </summary>
public class FastEnumHintNameCollisionTests
{
    private const string AttributeShim = """
        namespace Pragmatic
        {
            [System.AttributeUsage(System.AttributeTargets.Enum)]
            public sealed class FastEnumAttribute : System.Attribute { }
        }
        """;

    [Fact]
    public void SameSimpleEnumName_DifferentNamespaces_BothSourcesGeneratedWithoutCollision()
    {
        var source = AttributeShim + """

            namespace Sales
            {
                [Pragmatic.FastEnum]
                public enum Status { Open, Closed }
            }

            namespace Billing
            {
                [Pragmatic.FastEnum]
                public enum Status { Pending, Paid }
            }
            """;

        // The generated FastEnum code emits a System.Text.Json converter, so the test
        // compilation needs the System.Text.Json reference to verify it compiles cleanly.
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromTypeAssembly(typeof(JsonConverter<int>)));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse();

        // Distinct namespace-qualified hints: the two enums do not collide.
        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Should().Contain("Sales.Status.FastEnum.g.cs");
        generated.Keys.Should().Contain("Billing.Status.FastEnum.g.cs");
    }

    // An enum with an alias — two fields sharing a value, which C# allows — must not emit the same
    // constant pattern in two switch arms, or the generated file does not compile. Asserted by
    // compiling it: a text assertion would prove the arms are deduplicated without proving the result
    // is buildable, which is the point.
    [Fact]
    public void EnumWithAliasedMembers_GeneratesCodeThatCompiles()
    {
        var source = AttributeShim + """

            namespace Ordering
            {
                [Pragmatic.FastEnum]
                public enum Outcome
                {
                    Success = 0,
                    Ok = 0,
                    Failure = 1,
                    Error = 1,
                }
            }
            """;

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GeneratorTestHelper.FromTypeAssembly(typeof(JsonConverter<int>)));

        GeneratorTestHelper.HasCompilationErrors(result).Should().BeFalse(
            "two fields may share a value, and the alias must not become a duplicate switch arm");
    }
}
