using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     A projection may only name things the database has.
/// </summary>
/// <remarks>
///     <para>
///         The traits generator writes <c>public Guid Id => PersistenceId;</c> — a computed property,
///         mapped to no column. <c>Projection</c> named it, EF could not translate it, and so it
///         materialised the entity and evaluated the projection in memory: the reason projections
///         exist, switched off. Measured on a consumer, removing <c>Id</c> from a DTO took the same
///         <c>SELECT</c> from nine columns to three, and every projectable DTO there carries an id.
///     </para>
///     <para>
///         ⚠️ Every observable signal looked right — one statement, correct data — which is why it
///         survived. The suites watch the statement count, and the statement count does not move.
///     </para>
/// </remarks>
public class ProjectionReadsColumnsNotAliasesTests : MappingGeneratorTestBase
{
    private const string Source = """
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace TestApp;

        [Entity]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }

        [MapFrom<Story>]
        [GenerateProjection]
        public partial class StoryDto
        {
            public System.Guid Id { get; init; }
            public string Title { get; init; } = "";
        }
        """;

    /// <summary>The projection reads the column, not the alias over it.</summary>
    [Fact]
    public void TheProjection_ReadsPersistenceId_NotTheGeneratedAlias()
    {
        var generated = GetGeneratedSource(RunGenerator(Source), "StoryDto.Mapping");

        generated.Should().NotBeNull();
        TheProjectionOf(generated!).Should().Contain("Id = entity.PersistenceId",
            "the alias is computed and maps to no column: naming it takes the whole row back");
        TheProjectionOf(generated!).Should().NotContain("Id = entity.Id,",
            "which is what made EF give up on translating and evaluate in memory");
    }

    /// <summary>
    ///     The control: a property that is a real column is read as written.
    /// </summary>
    /// <remarks>
    ///     Without it, "the projection does not name Id" is satisfied by a projection that names
    ///     nothing — which is also how the row comes back whole.
    /// </remarks>
    [Fact]
    public void ARealColumn_IsReadAsWritten()
    {
        var generated = GetGeneratedSource(RunGenerator(Source), "StoryDto.Mapping");

        generated.Should().NotBeNull();
        TheProjectionOf(generated!).Should().Contain("Title = entity.Title");
    }

    /// <summary>
    ///     The second control: an <c>Id</c> the author declared on the entity is left alone.
    /// </summary>
    /// <remarks>
    ///     It keeps the rewrite to the generated alias. A hand-written <c>Id</c> is a real symbol and a
    ///     real column, and redirecting it to a <c>PersistenceId</c> that may not exist would emit code
    ///     that does not compile — the opposite defect.
    /// </remarks>
    [Fact]
    public void AnIdTheAuthorDeclared_IsNotRedirected()
    {
        var declared = """
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class Ticket
            {
                public System.Guid Id { get; set; }
                public string Subject { get; set; } = "";
            }

            [MapFrom<Ticket>]
            [GenerateProjection]
            public partial class TicketDto
            {
                public System.Guid Id { get; init; }
                public string Subject { get; init; } = "";
            }
            """;

        var generated = GetGeneratedSource(RunGenerator(declared), "TicketDto.Mapping");

        generated.Should().NotBeNull();
        TheProjectionOf(generated!).Should().Contain("Id = entity.Id");
        TheProjectionOf(generated!).Should().NotContain("PersistenceId");
    }

    /// <summary>
    ///     The <c>Projection</c> expression alone — <c>FromEntity</c> reads the alias quite legitimately.
    /// </summary>
    /// <remarks>
    ///     In memory there is nothing to translate, so the alias is the right thing to read there. The
    ///     defect is only in the expression handed to the database, and asserting over the whole file
    ///     would mix the two.
    /// </remarks>
    private static string TheProjectionOf(string generated)
    {
        var start = generated.IndexOf("Projection { get; } =", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "the DTO is projectable at all");

        var end = generated.IndexOf("RequiredNavigations", start, System.StringComparison.Ordinal);

        return end > start ? generated[start..end] : generated[start..];
    }
}
