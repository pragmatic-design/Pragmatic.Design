using System.Linq;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Traits;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Templates;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
/// The endpoint models are what the Endpoints feature turns into routes and auth policies, so the
/// read endpoint's route, verb and permission are asserted here rather than inferred from a snapshot.
/// </summary>
public class TagEndpointModelTests
{
    private static TagTraitModel BuildModel() => new()
    {
        ParentTypeName = "Reservation",
        ParentNamespace = "Showcase.Booking.Entities",
        ParentFullTypeName = "Showcase.Booking.Entities.Reservation",
        IdType = "System.Guid",
        BoundaryFullTypeName = "Showcase.Booking.BookingBoundary",
        BoundaryName = "Booking",
        ResourceSegment = "reservations",
        ResourceParamName = "reservationId",
    };

    [Fact]
    public void BuildTagEndpoints_EmitsListEndpoint_WithRouteVerbAndReadPermission()
    {
        var model = BuildModel();

        var endpoints = TraitEndpointModelBuilder.BuildTagEndpoints(model);

        var list = endpoints.Should().ContainSingle(e => e.HttpMethod == "Get").Subject;
        list.Route.Should().Be("/api/booking/reservations/{reservationId}/tags");
        list.TypeName.Should().Be("ListReservationTagsQuery");
        list.IsQuery.Should().BeTrue();
        list.QueryIsPaged.Should().BeTrue();
        list.Authorization!.IsRequired.Should().BeTrue();
        list.Authorization.RequiredPermissions.Should().ContainSingle()
            .Which.Should().Be("booking.reservation.tags.read");
    }

    /// <summary>
    ///     The permission the endpoint enforces and the constant the generated permissions class
    ///     exposes must be the same string, or a grant would never match the check.
    /// </summary>
    [Fact]
    public void ListEndpointPermission_MatchesGeneratedReadConstant()
    {
        var model = BuildModel();

        var list = TraitEndpointModelBuilder.BuildTagEndpoints(model).Single(e => e.HttpMethod == "Get");
        var constants = new TagPermissionsTemplate(model).RenderOutput().Text;

        constants.Should().Contain(
            $"public const string Read = \"{list.Authorization!.RequiredPermissions[0]}\";");
    }

    [Fact]
    public void BuildTagEndpoints_ProjectsFromJunction_ToTagDto()
    {
        var model = BuildModel();

        var list = TraitEndpointModelBuilder.BuildTagEndpoints(model).Single(e => e.HttpMethod == "Get");

        // The junction carries the parent FK; filtering on the shared Tag entity would be wrong.
        list.QueryEntityType.Should().Be("global::Showcase.Booking.Entities.ReservationTagLink");
        list.QueryResultType.Should().Be("global::Showcase.Booking.Entities.ReservationTagDto");
        list.QueryBoundaryType.Should().Be("global::Showcase.Booking.BookingBoundary");
        list.RouteParameters.Should().ContainSingle()
            .Which.PropertyName.Should().Be("ReservationId");
    }
}
