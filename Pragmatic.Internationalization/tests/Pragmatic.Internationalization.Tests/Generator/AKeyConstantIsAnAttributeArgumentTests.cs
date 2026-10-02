using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.Generator;

/// <summary>
///     The generated <c>TKeys</c> constants compile as attribute arguments — the reason they exist.
/// </summary>
/// <remarks>
///     <c>T.Greeting.Hello</c> is a property and cannot be an attribute argument, so a rule's
///     <c>MessageKey</c> repeated the key as a string.
/// </remarks>
public class AKeyConstantIsAnAttributeArgumentTests : I18NGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Internationalization.Attributes;
        using Probe.Texts;

        [assembly: TranslationKeys(Namespace = "Probe.Texts")]

        namespace Probe;

        [System.AttributeUsage(System.AttributeTargets.Class)]
        public sealed class HintAttribute(string key) : System.Attribute
        {
            public string Key { get; } = key;
        }

        [Hint(TKeys.Greeting.Hello)]
        public sealed class Greeter;
        """;

    private static readonly (string, string) English = ("translations/en.json", "{\"greeting\":{\"hello\":\"Hello\"}}");

    [Fact]
    public void TheConstant_IsAcceptedAsAnAttributeArgument()
    {
        var generated = GenerateAgainstTheRuntime(Source, English);

        generated.Errors.Should().BeEmpty("TKeys.Greeting.Hello is a constant");
        generated.Sources.Values.Should().Contain(text => text.Contains("public const string Hello = \"greeting.hello\";"));
    }

    /// <summary>The control: the typed key is a property, and the same argument does not compile.</summary>
    [Fact]
    public void TheTypedKey_IsNotAcceptedAsAnAttributeArgument()
        => GenerateAgainstTheRuntime(Source.Replace("TKeys.Greeting.Hello", "T.Greeting.Hello", StringComparison.Ordinal), English)
            .Errors.Should().NotBeEmpty();
}
