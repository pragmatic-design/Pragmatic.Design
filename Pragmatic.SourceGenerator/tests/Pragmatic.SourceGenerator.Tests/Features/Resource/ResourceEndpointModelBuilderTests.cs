using System;
using System.Collections.Immutable;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Resource.Models;
using Pragmatic.SourceGenerator.Features.Resource.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
/// Type names in the generated CRUD must be qualified with the namespace that declares them. Every
/// other fixture in this suite uses <c>System.Guid</c>, which is why "global::System." + the simple
/// name went unnoticed: with a strongly-typed id it produced <c>global::System.ReservationId</c>, a
/// type that does not exist.
/// </summary>
public class ResourceEndpointModelBuilderTests
{
    private const int AllCapabilities = 63;

    private static ResourceCrudModel BuildCrudModel(string idType, string idTypeName) => new()
    {
        Resource = new ResourceModel
        {
            Namespace = "Showcase.Booking.Entities",
            TypeName = "Reservation",
            FullTypeName = "global::Showcase.Booking.Entities.Reservation",
            Segment = "reservations",
            Capabilities = AllCapabilities,
            BoundaryFullTypeName = "global::Showcase.Booking.BookingBoundary",
            BoundaryName = "Booking",
            IdType = idType,
        },
        Properties = ImmutableArray.Create(
            new ResourcePropertyInfo { Name = "Id", TypeName = idTypeName, IsPrimaryKey = true },
            new ResourcePropertyInfo { Name = "Code", TypeName = "string", IsRequired = true }),
    };

    /// <summary>
    ///     Create answers with the entity, because it is a mutation and that is what a mutation returns.
    /// </summary>
    /// <remarks>
    ///     It is not a <c>DomainAction&lt;TId&gt;</c> answering with the bare id, which would make it the
    ///     one write endpoint in the framework shaped that way. What these three tests guard is that the
    ///     type is qualified with the <b>consumer's</b> namespace and that the two builders agree on it.
    /// </remarks>
    [Fact]
    public void Create_AnswersWithTheEntity()
    {
        var create = ResourceEndpointModelBuilder.Build(BuildCrudModel("System.Guid", "Guid"))
            .Single(e => e.HttpMethod == "Post" && !e.Route.EndsWith("/restore"));

        create.IsMutation.Should().BeTrue();
        create.MutationEntityType.Should().Be("global::Showcase.Booking.Entities.Reservation");
        create.DomainActionReturnType.Should().BeNull("a mutation endpoint is not a domain action");
    }

    /// <summary>
    ///     A strongly-typed id keeps the namespace that declares it. Never assume <c>System</c>.
    /// </summary>
    [Fact]
    public void AStronglyTypedId_QualifiesTheDeclaringNamespace_NotSystem()
    {
        var model = BuildCrudModel("Showcase.Booking.Entities.ReservationId", "ReservationId");

        var idType = ResourceMutationModelBuilder.Build(model)
            .First(m => m.TypeName.StartsWith("ResourceCreate", StringComparison.Ordinal))
            .EntityIdTypeName;

        idType.Should().Be("global::Showcase.Booking.Entities.ReservationId");
        idType.Should().NotContain("global::System.ReservationId",
            "the id type is the consumer's, not System's");
    }

    /// <summary>
    ///     The endpoint and the mutation are built by two different builders and must name one type.
    /// </summary>
    /// <remarks>
    ///     They are separate models assembled from the same resource, so nothing but a test stops them
    ///     drifting — and a disagreement here surfaces as a compile error inside generated code the
    ///     author cannot edit.
    /// </remarks>
    [Fact]
    public void TheEndpointAndTheMutation_AgreeOnTheEntityType()
    {
        var model = BuildCrudModel("Showcase.Booking.Entities.ReservationId", "ReservationId");

        var fromEndpoint = ResourceEndpointModelBuilder.Build(model)
            .Single(e => e.HttpMethod == "Post" && !e.Route.EndsWith("/restore")).MutationEntityType;
        var fromMutation = ResourceMutationModelBuilder.Build(model)
            .First(m => m.TypeName.StartsWith("ResourceCreate", StringComparison.Ordinal))
            .EntityFullTypeName;

        fromEndpoint.Should().Be(fromMutation);
    }

    [Fact]
    public void RouteParameters_StillBindByTheSimpleIdName()
    {
        var model = BuildCrudModel("Showcase.Booking.Entities.ReservationId", "ReservationId");

        var read = ResourceEndpointModelBuilder.Build(model)
            .First(e => e.HttpMethod == "Get" && e.Route.Contains("{reservationId}"));

        read.RouteParameters.Single().TypeName.Should().Be("ReservationId",
            "route parameters bind inside the generated file's own namespace");
    }

    /// <summary>
    ///     A write answers with the read shape while the resource still exists, and 204 once it does not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The rule in one place. Before it, Create answered with the entity and Update answered with
    ///         nothing — not a decision, two defaults meeting, and a caller who had to issue a GET to see
    ///         what they had just written.
    ///     </para>
    ///     <para>
    ///         Asserted on the model rather than over HTTP because the soft-delete half cannot be reached
    ///         from the Showcase: no entity there is both <c>[SoftDelete]</c> and a scaffolded
    ///         <c>[Resource]</c>. The hard-delete 204 and the update 200 are executed end to end;
    ///         these two rows are the ones that are not.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData(false, "Put", 200, true)]
    [InlineData(false, "Delete", 204, false)]
    [InlineData(true, "Put", 200, true)]
    [InlineData(true, "Delete", 200, true)]
    public void AWrite_AnswersWithTheReadShapeWhileTheResourceExists(
        bool isSoftDelete, string httpMethod, int expectedStatus, bool expectsBody)
    {
        var model = BuildCrudModel("System.Guid", "Guid") with { IsSoftDelete = isSoftDelete };

        var write = ResourceEndpointModelBuilder.Build(model).Single(e => e.HttpMethod == httpMethod);

        write.ComputedSuccessStatusCode.Should().Be(expectedStatus);
        if (expectsBody)
        {
            write.ResponseType.Should().Be("global::Showcase.Booking.Entities.ReservationReadDto");
            write.MutationResponseFactory.Should()
                .Be("global::Showcase.Booking.Entities.ReservationReadDto.FromEntity");
        }
        else
        {
            write.ResponseType.Should().BeNull("204 has no body by definition");
            write.MutationResponseFactory.Should().BeNull();
        }
    }

    /// <summary>
    ///     Restoring a soft-deleted resource answers with it, for the same reason: it is there again.
    /// </summary>
    [Fact]
    public void Restore_AnswersWithTheReadShape()
    {
        var model = BuildCrudModel("System.Guid", "Guid") with
        {
            IsSoftDelete = true,
            Resource = BuildCrudModel("System.Guid", "Guid").Resource with { Capabilities = 127 },
        };

        var restore = ResourceEndpointModelBuilder.Build(model).Single(e => e.Route.EndsWith("/restore"));

        restore.ComputedSuccessStatusCode.Should().Be(200);
        restore.ResponseType.Should().Be("global::Showcase.Booking.Entities.ReservationReadDto");
    }
}
