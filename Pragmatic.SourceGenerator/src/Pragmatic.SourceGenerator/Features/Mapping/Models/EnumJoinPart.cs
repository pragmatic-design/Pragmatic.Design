using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     One source path of a multi-path <c>[MapProperty]</c> whose type is an <c>enum</c>, with the
///     members needed to render its <b>name</b> inside a projection.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ A join is rendered as string concatenation, and the two paths do not agree on what an
///         enum concatenates to. In memory <c>string + enum</c> calls <c>ToString()</c> and produces the
///         member's name; the same expression translated to SQL concatenates the column, which is the
///         number it is stored as. One declaration, two answers — and the projected one is wrong rather
///         than missing, which is the worse kind: an empty column is noticed the first time somebody
///         looks at the screen, a <c>0</c> reads as data.
///     </para>
///     <para>
///         The members are carried so the projection can render a conditional chain, which EF Core
///         translates to a <c>CASE</c>. That is storage-independent on purpose: the generator does not
///         know whether the enum is stored as an integer or converted to a string, and comparing
///         against the members is correct either way.
///     </para>
/// </remarks>
internal sealed record EnumJoinPart
{
    /// <summary>The source path as the author wrote it, matched against <c>SourcePaths</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The enum's fully qualified type name, for naming its members in the generated chain.</summary>
    public required string TypeName { get; init; }

    /// <summary>The member names, in declaration order.</summary>
    public required EquatableArray<string> Members { get; init; }
}
