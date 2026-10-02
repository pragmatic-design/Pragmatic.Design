using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A member whose name on the wire is not its name in C#.
/// </summary>
/// <remarks>
///     <para>
///         Declared on the enum <b>member</b>, not on the property: the wire name belongs to the
///         value, and every shape that carries it should agree about it. Without it, a name that is
///         not a C# identifier — <c>in-progress</c> — meant writing a converter class for a rename.
///     </para>
///     <para>
///         ⚠️ Members without an alias keep their own name in both directions. That is what makes the
///         attribute usable on one member of an enum rather than on all of them, and it is the half a
///         test that only checked the aliased member would miss.
///     </para>
/// </remarks>
public class EnumAliasGeneratorTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping;
        using Pragmatic.Mapping.Attributes;

        namespace TestApp;

        public enum Status
        {
            [MapEnum(Alias = "in-progress")]
            InProgress,
            Done
        }

        public class Order
        {
            public Status Status { get; set; }
        }

        [MapFrom<Order>]
        [MapTo<Order>]
        public partial class OrderDto
        {
            public string Status { get; init; } = "";
        }
        """;

    /// <summary>
    ///     Reading gives the wire name, and only for the member that declares one.
    /// </summary>
    [Fact]
    public void TheAliasedMember_IsWrittenAndReadByItsWireName()
    {
        var result = RunGenerator(Source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated!.Should().Contain("\"in-progress\"",
            "the wire name appears on both sides of the mapping");
        generated.Should().Contain("ToString()",
            "and a member without an alias keeps its own name");
    }

    /// <summary>
    ///     ⚠️ The control: no alias anywhere means no switch at all.
    /// </summary>
    /// <remarks>
    ///     Nearly every enum in a codebase is named on the wire as it is named in C#. Emitting a
    ///     switch for all of them would be a mechanism where a <c>ToString()</c> was enough, and this
    ///     is what says it is not emitted.
    /// </remarks>
    [Fact]
    public void WithoutAnyAlias_NoSwitchIsEmitted()
    {
        // ⚠️ The attribute alone, without a newline: the file is CRLF, so the raw string carries
        // "\r\n" and a replace written with "\n" matches nothing, silently — and a "control" case
        // that silently changes nothing runs the very shape it exists to exclude, then passes for the
        // wrong reason.
        var result = RunGenerator(Source.Replace("[MapEnum(Alias = \"in-progress\")]", ""));

        HasCompilationErrors(result).Should().BeFalse(
            string.Join("; ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();
        generated!.Should().NotContain("in-progress");
        generated.Should().NotContain(" switch {",
            "no member asked for a name of its own, so nothing translates");
    }
}
