using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     A mutation <c>[Resource]</c> scaffolds is a member of its boundary, like one written by hand.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It was a member of nothing. The scaffolded models carry <c>BelongsToTypeName</c> from
///         <c>BoundaryOwnershipReader.BoundaryOf</c>, which returns <c>ToDisplayString()</c> — no
///         <c>global::</c> — while a boundary's own <c>FullTypeName</c> is the fully qualified form, and
///         <c>MatchMembersToBoundary</c> compares the two as strings. The comparison failed; and because
///         a member that names a boundary is never matched by namespace, it landed on **no** facade at
///         all — neither its group nor the root.
///     </para>
///     <para>
///         Invisible because the routes work: an endpoint does not go through this match. So «delete a
///         template» could be called over HTTP and not from another module, which is the one thing the
///         facade exists for.
///     </para>
///     <para>
///         The assertion is on the generated boundary <b>Definition</b> — the interface another module
///         compiles against — rather than on the model, because the model was right all along.
///     </para>
/// </remarks>
public class TheScaffoldedWriteIsOnTheFacadeTests
{
    /// <param name="entityNamespace">Where the resource lives — which is what decides its group.</param>
    private static string Source(string entityNamespace) => $$"""
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Showcase.Booking
        {
            [Boundary]
            public partial class BookingBoundary;
        }

        namespace {{entityNamespace}}
        {
            [Entity]
            [Resource("templates", Capabilities = ResourceCapabilities.Create | ResourceCapabilities.Delete)]
            public partial class Template : IEntity
            {
                public Guid PersistenceId { get; set; }
                public string Name { get; private set; } = "";
            }
        }
        """;

    private static string TheDefinition(string entityNamespace = "Showcase.Booking.Entities")
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(entityNamespace), [
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
            // [Resource] lives in Pragmatic.Persistence, not beside IEntity: without this the attribute
            // does not bind, ForAttributeWithMetadataName sees nothing, and the scaffold is simply absent.
            GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.ResourceAttribute>(),
            GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.BoundaryAttribute>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
            GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
        ]);

        var definition = GeneratorTestHelper.GetGeneratedSource(result, "_Boundary.BookingBoundary.Definition");
        definition.Should().NotBeNull("the boundary declares operations, so it has a facade");

        return definition!;
    }

    /// <summary>The scaffolded writes are on the facade.</summary>
    [Fact]
    public void TheScaffoldedMutations_AreMembersOfTheBoundary()
    {
        var definition = TheDefinition();

        definition.Should().Contain("ResourceCreateTemplateMutation",
            "a scaffolded write is reachable from another module, or it is an HTTP-only operation");
        definition.Should().Contain("ResourceDeleteTemplateMutation");
    }

    /// <summary>
    ///     And they are grouped by the same rule a hand-written mutation follows: the namespace segment
    ///     under the boundary.
    /// </summary>
    /// <remarks>
    ///     Without this, "the name appears in the file" is satisfied by putting every scaffolded write on
    ///     the boundary root — which is the shape rule 10.1 exists to prevent, and a facade of forty flat
    ///     methods is what it prevents.
    /// </remarks>
    [Fact]
    public void TheyAreGrouped_LikeAHandWrittenMutationInTheSameNamespace()
    {
        var definition = TheDefinition("Showcase.Booking.Templates");

        definition.Should().Contain("IBookingTemplatesActions",
            "the segment under the boundary is the group, for a scaffolded write as for any other");
        definition.Should().Contain("ResourceDeleteTemplateMutation");
    }

    /// <summary>
    ///     The other half of the same rule: a segment the inference discards leaves the operation on the
    ///     root, scaffolded or not.
    /// </summary>
    /// <remarks>
    ///     <c>Entities</c> is one of the recognised segments, so an entity that lives there names no
    ///     group. Asserted because the alternative — inventing a group called <c>Entities</c> — is what a
    ///     naive fix would produce, and it would put the scaffolded writes somewhere no hand-written
    ///     operation goes.
    /// </remarks>
    [Fact]
    public void InADiscardedSegment_TheyStayOnTheRoot()
    {
        var definition = TheDefinition("Showcase.Booking.Entities");

        definition.Should().NotContain("IBookingEntitiesActions",
            "Entities is discarded by the inference — it is not a resource folder");
    }

    /// <summary>The control: a boundary with no operations still gets no facade.</summary>
    /// <remarks>
    ///     Without it, "the definition contains the mutation" could be satisfied by a generator that emits
    ///     a facade for everything and matches nothing — the file would exist and the membership would
    ///     still be wrong.
    /// </remarks>
    [Fact]
    public void ABoundaryWithNoOperations_GetsNoFacade()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>("""
            using Pragmatic.Actions.Attributes;

            namespace Empty;

            [Boundary]
            public partial class EmptyBoundary;
            """, [GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.BoundaryAttribute>()]);

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys
            .Should().NotContain(k => k.Contains("_Boundary.EmptyBoundary.Definition"));
    }
}
