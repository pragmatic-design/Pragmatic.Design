using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGenerator.Tests.Core;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The generated <c>[GeneratedValue]</c> default-value generator is <em>declared</em> by
///     <c>GeneratedValueTransform</c> + <c>GeneratedValueTemplate</c> and <em>referenced</em> by
///     <c>GeneratedValueRegistrationTemplate</c>, which registers it and its binding. Each side builds
///     <c>{Entity}{Prop}ValueGenerator</c> for itself, so a rename on one side alone would emit
///     generated code pointing at a type that does not exist. This test ties the two names together.
/// </summary>
/// <remarks>
///     The referencing side is the registration, not the create mutation's invoker: reached only from
///     there, an entity created any other way would be inserted with the column empty. Both names come
///     out of the same generated compilation, so this compares two outputs rather than an internal API
///     against an output.
/// </remarks>
public class GeneratedValueNamingBindingTests
{
    private const string Source = """
        namespace Pragmatic.Persistence.EFCore
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : System.Attribute { }
        }

        namespace Pragmatic.Persistence.Entity
        {
            [System.AttributeUsage(System.AttributeTargets.Property)]
            public sealed class GeneratedValueAttribute : System.Attribute
            {
                public GeneratedValueAttribute(string format) { Format = format; }
                public string Format { get; }
                public bool AutoGenerate { get; set; } = true;
                public string? SequenceName { get; set; }
            }
        }

        namespace MyApp.Sales
        {
            using Pragmatic.Persistence.Entity;

            public sealed class Order
            {
                [GeneratedValue("ORD-{YYYY}-{RANDOM:4}")]
                public string OrderNumber { get; private set; } = "";
            }
        }
        """;

    /// <summary>The FQN the generated registration resolves the generator by.</summary>
    private static string ReferencedGeneratorFqn()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source);
        var registration = GeneratorTestHelper.GetGeneratedSource(result, "ValueGenerators");

        registration.Should().NotBeNull(
            "a [GeneratedValue] property must produce a registration, whether or not any mutation "
            + "creates the entity — that it did not is what left the column empty");

        var referenced = Regex.Match(registration!, @",\s*(global::[\w.]*ValueGenerator)>\(\);");
        referenced.Success.Should().BeTrue("the registration names the generator it registers");

        return referenced.Groups[1].Value;
    }

    /// <summary>The FQN the generator actually emits into the consumer's compilation.</summary>
    private static string DeclaredGeneratorFqn()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source);
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ValueGenerator");

        generated.Should().NotBeNull("the [GeneratedValue] property must produce a default-value generator");

        var ns = Regex.Match(generated!, @"^namespace\s+([\w.]+)", RegexOptions.Multiline);
        var className = Regex.Match(generated!, @"class\s+(\w*ValueGenerator)\b");

        ns.Success.Should().BeTrue("the generated file declares a namespace");
        className.Success.Should().BeTrue("the generated file declares the generator class");

        return $"global::{ns.Groups[1].Value}.{className.Groups[1].Value}";
    }

    // The binding test: if either side changes its naming convention alone, this fails.
    [Fact]
    public void ReferencedGeneratorType_IsTheTypeTheGeneratorDeclares()
        => ReferencedGeneratorFqn().Should().Be(DeclaredGeneratorFqn());

    [Fact]
    public void BothSides_GoThroughTheSharedNamingHelper()
    {
        var expected = GeneratedValueNaming.GeneratorFullyQualifiedName("MyApp.Sales", "Order", "OrderNumber");

        DeclaredGeneratorFqn().Should().Be(expected);
        ReferencedGeneratorFqn().Should().Be(expected);
    }

    [Fact]
    public void GeneratorFullyQualifiedName_GlobalNamespace_HasNoLeadingDot()
        => GeneratedValueNaming.GeneratorFullyQualifiedName("", "Order", "OrderNumber")
            .Should().Be("global::OrderOrderNumberValueGenerator");
}
