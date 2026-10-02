using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A string that names no member of the target enum.
/// </summary>
/// <remarks>
///     <para>
///         <c>Enum.Parse</c> throws, and on a write path the string usually came from a caller: an
///         unparseable value was then answered with a <c>500</c>, which says "the server broke" about
///         a request that was simply wrong.
///     </para>
///     <para>
///         ⚠️ The default is still <c>Throw</c>. Turning a loud failure into a quiet one on every
///         existing shape is not a fix — it is the same defect with the evidence removed.
///     </para>
/// </remarks>
public class EnumOnUnknownGeneratorTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        public enum Channel { Unknown, Web, Phone }

        public class Order
        {
            public Channel Channel { get; set; }
        }

        [MapTo<Order>]
        public partial class OrderDto
        {
            public string Channel { get; set; } = "";
        }
        """;

    /// <summary>
    ///     The default: parse, and throw on a name that matches nothing.
    /// </summary>
    /// <remarks>
    ///     The control case. Without it, an implementation that always used <c>TryParse</c> would
    ///     pass the other one and silently change what every existing DTO does.
    /// </remarks>
    [Fact]
    public void WithoutTheAttribute_ItStillThrows()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("Enum.Parse<", "the default is unchanged");
        generated.Should().NotContain("Enum.TryParse<");
    }

    /// <summary>
    ///     ⚠️ Declared: an unknown name becomes the enum's default instead of an exception.
    /// </summary>
    [Fact]
    public void WithOnUnknownDefault_ItFallsBack()
    {
        var result = RunGenerator(Source.Replace(
            "public string Channel { get; set; } = \"\";",
            "[MapEnum(OnUnknown = UnknownEnumValue.Default)]\n    public string Channel { get; set; } = \"\";"));

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().Contain("Enum.TryParse<",
            "the declaration is what turns the parse into a try");
        generated.Should().NotContain("Enum.Parse<",
            "and the throwing form is gone, not merely joined");
    }

    /// <summary>
    ///     Two enum properties on one shape: the two fallbacks cannot share an out variable.
    /// </summary>
    /// <remarks>
    ///     Both land in the same object initializer, so one name would be <c>CS0128</c> inside a
    ///     generated file. Asserted by compiling, not by reading the text.
    /// </remarks>
    [Fact]
    public void TwoEnumPropertiesOnOneShape_BothCompile()
    {
        var source = """
            using Pragmatic.Mapping;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public enum Channel { Unknown, Web }
            public enum Priority { Normal, High }

            public class Order
            {
                public Channel Channel { get; set; }
                public Priority Priority { get; set; }
            }

            [MapTo<Order>]
            public partial class OrderDto
            {
                [MapEnum(OnUnknown = UnknownEnumValue.Default)]
                public string Channel { get; set; } = "";

                [MapEnum(OnUnknown = UnknownEnumValue.Default)]
                public string Priority { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            "two fallbacks in one initializer need two out variables");
    }
}
