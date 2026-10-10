using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     The constructor path of <c>ToEntity()</c> converts its arguments, like every other
///     path.
/// </summary>
/// <remarks>
///     <para>
///         The per-property conversion — enum parsing, and by the same token anything else the mapping
///         knows how to convert — lived on the <b>assignment</b> path only. The constructor branch
///         named the DTO's properties raw, so a target whose parameter type differs from the DTO's was
///         <c>CS1503</c>: <c>new Delivery(Reference, Channel)</c> with a <c>string</c> where a
///         <c>DeliveryChannel</c> was wanted.
///     </para>
///     <para>
///         ⚠️ Which is the one path that exists <b>for</b> such targets. A constructor is what you
///         reach for when the target has no settable properties — a value object, an immutable domain
///         type, a record validating in its <c>ctor</c> — and those are exactly the types whose
///         parameters are not the DTO's wire types: a <c>string</c> for an enum, a <c>decimal</c> for
///         <c>Money</c>, a <c>string</c> for a strongly-typed id.
///     </para>
///     <para>
///         ⚠️ And it is not a <c>[MapConstructor]</c> defect: the same shape fails without the
///         attribute, because best-match picks the parameterised constructor anyway. The attribute
///         only makes the choice explicit and inherits the gap.
///     </para>
/// </remarks>
public class AConstructorArgumentIsConvertedTests : MappingGeneratorTestBase
{
    private const string Declared = """
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace Shipping
        {
            public enum DeliveryChannel { Unknown, Email, Post }

            public class Delivery
            {
                public string Reference { get; set; } = "";
                public DeliveryChannel Channel { get; set; }

                public Delivery() { }

                [MapConstructor]
                public Delivery(string reference, DeliveryChannel channel)
                {
                    Reference = reference.Trim().ToUpperInvariant();
                    Channel = channel;
                }
            }

            [MapTo<Delivery>]
            public partial class DeliveryDto
            {
                public string Reference { get; init; } = "";

                [MapEnum(OnUnknown = UnknownEnumValue.Default)]
                public string Channel { get; init; } = "";
            }
        }
        """;

    [Fact]
    public void AConstructorTakingAnEnum_CompilesFromADtoCarryingAString()
    {
        var result = RunGenerator(Declared);

        HasCompilationErrors(result).Should().BeFalse(
            "the argument is converted on the way in, the way an assignment always was: "
            + string.Join("\n", GetCompilationErrors(result).Select(d => d.ToString())));
    }

    [Fact]
    public void TheConversionIsInTheConstructorCall_NotAfterIt()
    {
        var source = GetGeneratedSource(RunGenerator(Declared), "DeliveryDto.Mapping") ?? "";

        source.Should().Contain("new global::Shipping.Delivery(",
            "the declared constructor is the one chosen");
        source.Should().Contain("Enum.TryParse<global::Shipping.DeliveryChannel>",
            "and the string is parsed where the argument is passed — a property the constructor "
            + "takes cannot be assigned afterwards, which is the whole reason it is a parameter");
    }

    /// <summary>
    ///     The same shape without the attribute, which is how the defect was actually met.
    /// </summary>
    /// <remarks>
    ///     Best-match picks the parameterised constructor on its own, so removing <c>[MapConstructor]</c>
    ///     changes which declaration is explicit and not which code is generated. Without this case the
    ///     fix reads as being about the attribute.
    /// </remarks>
    [Fact]
    public void WithoutTheAttribute_TheSameConstructorIsChosenAndConverted()
    {
        // The attribute alone is removed, not its line: the raw string carries the line endings of the
        // checkout, CRLF on Windows, where a pattern ending in "\n" removed nothing and this case ran
        // with the attribute still there.
        var withoutAttribute = Declared.Replace("[MapConstructor]", "");
        withoutAttribute.Should().NotContain("MapConstructor");

        var result = RunGenerator(withoutAttribute);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("\n", GetCompilationErrors(result).Select(d => d.ToString())));

        (GetGeneratedSource(result, "DeliveryDto.Mapping") ?? "")
            .Should().Contain("Enum.TryParse<global::Shipping.DeliveryChannel>");
    }

    /// <summary>
    ///     The control on the risk this change introduces: a parameter with no value to give is not
    ///     quietly given <c>default</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Asking the property model for an expression means there are now properties it can
    ///         answer "nothing" about — an <c>[MapIgnore]</c>d one, or one whose mapping resolved to
    ///         nothing. Falling back to <c>default</c> there would compile and lose the value, which
    ///         is the shape this whole epic exists to catch; falling back to the property's own name
    ///         makes the compiler say what the author declared.
    ///     </para>
    ///     <para>
    ///         ⚠️ The first version of this control asserted that a <c>DateTimeOffset</c> going into a
    ///         <c>string</c> was refused. It is not — the generator converts it — so the control was
    ///         measuring a premise of mine rather than a rule of the generator's.
    ///     </para>
    /// </remarks>
    [Fact]
    public void AnIgnoredPropertyBehindAConstructorParameter_IsNotSilentlyDefaulted()
    {
        var result = RunGenerator("""
            using Pragmatic.Mapping;
            using Pragmatic.Mapping.Attributes;

            namespace Shipping
            {
                public class Parcel
                {
                    public string Weight { get; set; } = "";
                    public Parcel() { }
                    [MapConstructor]
                    public Parcel(string weight) { Weight = weight; }
                }

                [MapTo<Parcel>]
                public partial class ParcelDto
                {
                    [MapIgnore]
                    public string Weight { get; init; } = "";
                }
            }
            """);

        var source = GetGeneratedSource(result, "ParcelDto.Mapping") ?? "";

        source.Should().NotContain("new global::Shipping.Parcel(default)",
            "an ignored property is not a value: passing `default` would build a parcel weighing "
            + "nothing and say so nowhere");
    }

    /// <summary>
    ///     The control on the other side: a constructor whose parameters already match is unchanged.
    /// </summary>
    [Fact]
    public void AConstructorThatNeedsNoConversion_StillTakesThePropertyItself()
    {
        var source = GetGeneratedSource(RunGenerator("""
            using Pragmatic.Mapping.Attributes;

            namespace Shipping
            {
                public class Note
                {
                    public string Text { get; set; } = "";
                    public Note() { }
                    [MapConstructor]
                    public Note(string text) { Text = text; }
                }

                [MapTo<Note>]
                public partial class NoteDto { public string Text { get; init; } = ""; }
            }
            """), "NoteDto.Mapping") ?? "";

        source.Should().Contain("new global::Shipping.Note(",
            "nothing about the plain case changes: the expression for a property that needs no "
            + "conversion is the property");
    }
}
