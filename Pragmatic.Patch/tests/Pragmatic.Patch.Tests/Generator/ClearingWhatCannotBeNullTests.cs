using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Patch.Tests.Generator;

/// <summary>
///     A tri-state is a tri-state only where there are three states.
/// </summary>
/// <remarks>
///     <para>
///         Every generated property carried the same sentence — "Undefined = don't change, Null =
///         clear, Value = set" — including the ones whose column cannot be null, and <c>ApplyTo</c>
///         wrote whatever <c>HasValue</c> was true for. So <c>{"sourceRef": null}</c> against a
///         <c>NOT NULL</c> column came back as a <b>400 Database Constraint Violation</b>: a caller
///         told their body was wrong by the storage engine, naming a column instead of the field they
///         sent, after following a promise the generator had made them.
///     </para>
///     <para>
///         The other half was already right and undocumented: a JSON null for a non-nullable
///         <b>value</b> type reads as undefined, stated only in a comment inside the converter. Two
///         states, and now the property says so.
///     </para>
/// </remarks>
public class ClearingWhatCannotBeNullTests : PatchGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Patch.Attributes;

        namespace TestApp;

        public class Story
        {
            public string SourceRef { get; set; } = "";
            public int Rank { get; set; }
            public string? Note { get; set; }
        }

        [GeneratePatch<Story>]
        public partial record StoryPatch;
        """;

    /// <summary>A null against a column that cannot take one is refused, by name, at the boundary.</summary>
    [Fact]
    public void ClearingANonNullableReference_IsRefusedWhereTheBodyIsRead()
    {
        var converter = GetGeneratedSource(RunGenerator(Source), "StoryPatch.JsonConverter");

        converter.Should().NotBeNull();
        converter!.Should().Contain("cannot be cleared",
            "the refusal belongs where the body is read, naming the field the caller sent — not to "
            + "the database, naming a column");
        converter.Should().Contain("Story.SourceRef is not nullable.");
    }

    /// <summary>
    ///     The control: a nullable column can still be cleared, which is what the tri-state is for.
    /// </summary>
    /// <remarks>
    ///     Without it, "a null is refused" is satisfied by refusing every null, which deletes the
    ///     feature instead of correcting its promise.
    /// </remarks>
    [Fact]
    public void ClearingANullableColumn_StillWorks()
    {
        var converter = GetGeneratedSource(RunGenerator(Source), "StoryPatch.JsonConverter");

        converter.Should().NotBeNull();
        // The variable, not the type: the declared type reaches the template fully qualified, and
        // pinning its spelling would make this test about the display format instead of the states.
        converter!.Should().Contain("note = Optional<",
            "three states where there are three states");
        converter.Should().Contain(">.Null;");
        converter.Should().NotContain("sourceRef = Optional<global::System.String>.Null;",
            "and the column that cannot be cleared is not offered the clear");
    }

    /// <summary>The promise on each property matches what that property does.</summary>
    [Fact]
    public void EachPropertyDeclares_TheStatesItActuallyHas()
    {
        var patch = GetGeneratedSource(RunGenerator(Source), "StoryPatch.Patch");

        patch.Should().NotBeNull();

        patch!.Should().Contain("Patch value for Story.Note. Undefined = don't change, Null = clear, Value = set.");
        patch.Should().Contain(
            "Patch value for Story.SourceRef. Undefined = don't change, Value = set. "
            + "A JSON null is refused: the column cannot be null.");
        patch.Should().Contain(
            "Patch value for Story.Rank. Undefined = don't change, Value = set. "
            + "A JSON null reads as undefined: the column cannot be null.");
    }

    /// <summary>
    ///     The second control: a value type keeps its two states, and keeps reading a null as undefined.
    /// </summary>
    /// <remarks>
    ///     It was already the behaviour, and it is the one case where reading a null as "nothing asked"
    ///     is right: <c>Optional&lt;int&gt;</c> has no third state to offer, so there is no request to
    ///     lose.
    /// </remarks>
    [Fact]
    public void ANonNullableValueType_StillReadsNullAsUndefined()
    {
        var converter = GetGeneratedSource(RunGenerator(Source), "StoryPatch.JsonConverter");

        converter.Should().NotBeNull();
        converter!.Should().Contain("JSON null for non-nullable value type — treat as undefined");
        converter.Should().NotContain("Story.Rank is not nullable.");
    }
}
