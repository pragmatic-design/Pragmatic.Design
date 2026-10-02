using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Models;
using Pragmatic.SourceGenerator.Features.Manifest.Templates;
using Pragmatic.SourceGenerator.Features.Manifest.Transforms;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Manifest;

/// <summary>
///     <c>ManifestTransform.Build</c> is the aggregation point every downstream consumer depends on
///     (compile-time OpenAPI, CLI/WASM client generation, runtime OpenAPI enrichment). Beyond
///     <c>ManifestJsonTemplate.RenderEndpoint</c>, these tests cover route resolution, boundary
///     derivation, type discovery through the <see cref="Microsoft.CodeAnalysis.Compilation" />,
///     action/permission extraction and the JSON validity of the whole document.
/// </summary>
public class ManifestTransformTests
{
    private static EndpointModel Endpoint(
        string typeName = "GetReservation",
        string ns = "Showcase.Booking.Reservations.Endpoints",
        string httpMethod = "Get",
        string route = "{id}") => new()
    {
        Namespace = ns,
        TypeName = typeName,
        FullTypeName = $"global::{ns}.{typeName}",
        Accessibility = "public",
        HttpMethod = httpMethod,
        Route = route,
        IsVoid = false,
        IsDomainAction = false
    };

    private static ManifestModel Build(
        ImmutableArray<EndpointModel> endpoints,
        ImmutableArray<EntityMetadataModel> entities = default,
        Microsoft.CodeAnalysis.Compilation? compilation = null)
        => ManifestTransform.Build(
            "Showcase.Booking",
            endpoints,
            entities.IsDefault ? ImmutableArray<EntityMetadataModel>.Empty : entities,
            compilation);

    // ---------------------------------------------------------------- routes / identity

    [Fact]
    public void Build_NoGroup_DerivesBoundaryFromNamespaceAndPrefixesRoute()
    {
        var manifest = Build([Endpoint()]);

        var endpoint = manifest.Endpoints.Should().ContainSingle().Subject;
        endpoint.FullRoute.Should().Be("/{id}");
        endpoint.OperationId.Should().Be("Booking.GetReservation", "the boundary is the 2nd namespace segment");
        endpoint.HttpMethod.Should().Be("Get");
        endpoint.SuccessStatusCode.Should().Be(200);
    }

    [Fact]
    public void Build_NestedGroups_ConcatenatesRoutePrefixesAndUsesGroupTagAsBoundary()
    {
        var parent = new EndpointGroupModel { TypeName = "ApiGroup", RoutePrefix = "/api" };
        var child = new EndpointGroupModel
        {
            TypeName = "ReservationsGroup", RoutePrefix = "/reservations", Tag = "Reservations", Parent = parent
        };

        var manifest = Build([Endpoint() with { Group = child }]);

        var endpoint = manifest.Endpoints.Should().ContainSingle().Subject;
        endpoint.FullRoute.Should().Be("/api/reservations/{id}");
        endpoint.OperationId.Should().Be("Reservations.GetReservation");
    }

    [Fact]
    public void Build_InvalidEndpoint_IsExcludedFromEveryProjection()
    {
        var invalid = Endpoint("BadEndpoint") with
        {
            InvalidReason = InvalidReason.MissingRoute,
            IsDomainAction = true,
            Authorization = new AuthorizationModel { RequiredPermissions = new[] { "booking.read" }.ToEquatableArray() }
        };

        var manifest = Build([invalid]);

        manifest.Endpoints.Should().BeEmpty();
        manifest.Actions.Should().BeEmpty();
        manifest.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void Build_Parameters_MapsRouteQueryHeaderAndCookieSources()
    {
        var endpoint = Endpoint() with
        {
            RouteParameters = new[]
            {
                new RouteParameterModel { Name = "id", PropertyName = "Id", TypeName = "System.Guid", Constraint = "guid" }
            }.ToEquatableArray(),
            QueryParameters = new[]
            {
                new QueryParameterModel { Name = "page", PropertyName = "Page", TypeName = "int", DefaultValue = "1" }
            }.ToEquatableArray(),
            HeaderParameters = new[]
            {
                new HeaderParameterModel { HeaderName = "X-Tenant", PropertyName = "Tenant", TypeName = "string", IsRequired = true }
            }.ToEquatableArray(),
            CookieParameters = new[]
            {
                new CookieParameterModel { CookieName = "sid", PropertyName = "SessionId", TypeName = "string" }
            }.ToEquatableArray()
        };

        var parameters = Build([endpoint]).Endpoints[0].Parameters;

        parameters.Select(p => (p.Name, p.In)).Should().Equal(
            ("id", "path"), ("page", "query"), ("X-Tenant", "header"), ("sid", "cookie"));
        parameters[0].IsRequired.Should().BeTrue("a non-optional route parameter is required");
        parameters[0].Constraint.Should().Be("guid");
        parameters[1].DefaultValue.Should().Be("1");
        parameters[2].IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Build_RequestBody_MarksRequiredPropertiesWithAValidationRule()
    {
        var endpoint = Endpoint("CreateReservation", httpMethod: "Post", route: "") with
        {
            BodyProperties = new[]
            {
                new BodyPropertyModel { Name = "GuestId", TypeName = "System.Guid", IsRequired = true },
                new BodyPropertyModel { Name = "Notes", TypeName = "string", IsNullable = true }
            }.ToEquatableArray()
        };

        var body = Build([endpoint]).Endpoints[0].RequestBody;

        body.Should().NotBeNull();
        body!.Properties.Should().HaveCount(2);
        body.Properties[0].Validation.Select(v => v.Rule).Should().Equal("required");
        body.Properties[1].Validation.Should().BeEmpty();
    }

    [Fact]
    public void Build_NoBodyProperties_OmitsRequestBody()
    {
        Build([Endpoint()]).Endpoints[0].RequestBody.Should().BeNull();
    }

    [Fact]
    public void Build_IdempotencyHeader_IsSuppressedOnSafeVerbs()
    {
        var idempotency = new IdempotencyModel { HeaderName = "Idempotency-Key" };

        Build([Endpoint(httpMethod: "Get") with { Idempotency = idempotency }])
            .Endpoints[0].IdempotencyHeader.Should().BeNull();

        Build([Endpoint(httpMethod: "Post") with { Idempotency = idempotency }])
            .Endpoints[0].IdempotencyHeader.Should().Be("Idempotency-Key");
    }

    // ---------------------------------------------------------------- errors

    [Fact]
    public void Build_Errors_DerivesUpperSnakeCodeAndCarriesCustomExtensions()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Pragmatic { public abstract class Error { public string? Code { get; init; } } }
            namespace Showcase.Booking.Errors
            {
                public sealed class RoomNotAvailableError : Pragmatic.Error
                {
                    public System.Guid RoomId { get; init; }
                    public System.DateTimeOffset AvailableFrom { get; init; }
                }
            }
            """);

        var endpoint = Endpoint() with
        {
            ErrorTypes = new[]
            {
                new ErrorTypeModel
                {
                    TypeName = "global::Showcase.Booking.Errors.RoomNotAvailableError",
                    SimpleName = "RoomNotAvailableError",
                    StatusCode = 409
                }
            }.ToEquatableArray()
        };

        var manifest = Build([endpoint], compilation: compilation);

        var error = manifest.Endpoints[0].Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be("ROOM_NOT_AVAILABLE");
        error.StatusCode.Should().Be(409);
        error.Extensions.Select(e => e.Name).Should().BeEquivalentTo("roomId", "availableFrom");

        // The same error also surfaces as a manifest type, so a client generator can model it.
        var errorType = manifest.Types.Should().ContainSingle(t => t.Kind == ManifestTypeKind.Error).Subject;
        errorType.ErrorCode.Should().Be("ROOM_NOT_AVAILABLE");
        errorType.ErrorStatusCode.Should().Be(409);
    }

    [Fact]
    public void Build_SameErrorOnTwoEndpoints_IsEmittedOnceInTypes()
    {
        var error = new ErrorTypeModel
        {
            TypeName = "global::Showcase.Booking.Errors.NotFoundError", SimpleName = "NotFoundError", StatusCode = 404
        };
        var endpoints = ImmutableArray.Create(
            Endpoint("A") with { ErrorTypes = new[] { error }.ToEquatableArray() },
            Endpoint("B") with { ErrorTypes = new[] { error }.ToEquatableArray() });

        var manifest = Build(endpoints);

        manifest.Types.Where(t => t.Kind == ManifestTypeKind.Error).Should().HaveCount(1);
    }

    // ---------------------------------------------------------------- type discovery

    [Fact]
    public void Build_ResponseType_IsResolvedThroughTheCompilationWithItsEnums()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Showcase.Booking.Dtos
            {
                public enum ReservationStatus { Draft, Confirmed, Cancelled }

                public class ReservationDto
                {
                    public System.Guid Id { get; set; }
                    public string GuestName { get; set; } = "";
                    public string? Notes { get; set; }
                    public ReservationStatus Status { get; set; }
                    public System.Collections.Generic.List<string> Tags { get; set; } = new();
                }
            }
            """);

        var manifest = Build(
            [Endpoint() with { ResponseType = "global::Showcase.Booking.Dtos.ReservationDto" }],
            compilation: compilation);

        var dto = manifest.Types.Should().ContainSingle(t => t.SimpleName == "ReservationDto").Subject;
        dto.Kind.Should().Be(ManifestTypeKind.Dto);
        // A DTO's collections are part of its shape and are published. The rule that skips them was
        // written for entity navigations, where descending walks the object graph, and was applied to
        // DTOs too — so a type whose reason to exist was the collection it carried went into the
        // contract without it, and a generated client could not read the field.
        dto.Properties.Select(p => p.Name).Should().Equal("Id", "GuestName", "Notes", "Status", "Tags");
        dto.Properties.Single(p => p.Name == "Notes").IsNullable.Should().BeTrue();
        dto.Properties.Single(p => p.Name == "GuestName").IsRequired.Should().BeTrue();
        dto.Properties.Single(p => p.Name == "Status").IsEnum.Should().BeTrue();

        var status = manifest.Types.Should().ContainSingle(t => t.SimpleName == "ReservationStatus").Subject;
        status.Kind.Should().Be(ManifestTypeKind.Enum);
        status.EnumValues.Should().Equal("Draft", "Confirmed", "Cancelled");
    }

    /// <summary>
    ///     A collection of <c>object</c> stays out too: there is nothing to describe.
    /// </summary>
    /// <remarks>
    ///     A grid adapter's payload really is a list of object — the values are whatever the component
    ///     sent. Published, it hands the client an <c>object[]</c> and a diagnostic asking the author
    ///     to describe a type that by construction has nothing to describe. Found by the gate, on the
    ///     Showcase, the moment DTO collections started being published.
    /// </remarks>
    [Fact]
    public void Build_ACollectionOfObject_StaysOutOfTheManifest()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Showcase.Booking.Dtos
            {
                public class LoadOptions
                {
                    public string Sort { get; set; } = "";
                    public System.Collections.Generic.IReadOnlyList<object?>? Filter { get; set; }
                }
            }
            """);

        var manifest = Build(
            [Endpoint() with { ResponseType = "global::Showcase.Booking.Dtos.LoadOptions" }],
            compilation: compilation);

        manifest.Types.Should().ContainSingle(t => t.SimpleName == "LoadOptions")
            .Which.Properties.Select(p => p.Name).Should().Equal("Sort");
    }

    /// <summary>
    ///     An entity's collection stays out, which is what the skip was for.
    /// </summary>
    /// <remarks>
    ///     The control for the test above. Without it, narrowing the rule to DTOs could have been
    ///     written as removing it, and the manifest would descend a navigation into the whole object
    ///     graph.
    /// </remarks>
    [Fact]
    public void Build_EntityNavigations_StayOutOfTheManifest()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Pragmatic.Domain
            {
                public interface IEntity { System.Guid PersistenceId { get; } }
            }

            namespace Showcase.Booking.Dtos
            {
                public class Line { public string Text { get; set; } = ""; }

                public class Order : Pragmatic.Domain.IEntity
                {
                    public System.Guid Id { get; set; }
                    public string Reference { get; set; } = "";
                    public System.Collections.Generic.List<Line> Lines { get; set; } = new();
                }
            }
            """);

        var manifest = Build(
            [Endpoint() with { ResponseType = "global::Showcase.Booking.Dtos.Order" }],
            compilation: compilation);

        var order = manifest.Types.Should().ContainSingle(t => t.SimpleName == "Order").Subject;

        order.Kind.Should().Be(ManifestTypeKind.Entity);
        order.Properties.Select(p => p.Name).Should().Equal("Id", "Reference");
    }

    [Fact]
    public void Build_WithoutCompilation_SkipsTypeDiscoveryButStillBuildsTheManifest()
    {
        var manifest = Build([Endpoint() with { ResponseType = "global::Showcase.Booking.Dtos.ReservationDto" }]);

        manifest.Types.Should().BeEmpty();
        manifest.Endpoints.Should().ContainSingle().Which.ResponseType!.Type
            .Should().Be("global::Showcase.Booking.Dtos.ReservationDto");
    }

    /// <summary>
    ///     <c>EndpointTransform</c> sets the response of a <c>[Query]</c> endpoint to
    ///     <c>PagedResult&lt;TResult&gt;</c> (paged) or <c>IReadOnlyList&lt;TResult&gt;</c> (unpaged).
    ///     Both wrappers match the well-known prefixes, so the item DTO only reaches the manifest if the
    ///     generic is unwrapped BEFORE the well-known check — and the wrapper itself must stay out.
    /// </summary>
    [Fact]
    public void Build_QueryResponseWrappers_UnwrapToTheItemDto()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Showcase.Booking.Dtos
            {
                public class ReservationListItemDto { public System.Guid Id { get; set; } }
            }
            """);

        var paged = Build(
            [
                Endpoint() with
                {
                    ResponseType =
                        "global::Pragmatic.Persistence.Query.Results.PagedResult<global::Showcase.Booking.Dtos.ReservationListItemDto>",
                    QueryIsPaged = true
                }
            ],
            compilation: compilation);

        var unpaged = Build(
            [
                Endpoint() with
                {
                    ResponseType =
                        "global::System.Collections.Generic.IReadOnlyList<global::Showcase.Booking.Dtos.ReservationListItemDto>"
                }
            ],
            compilation: compilation);

        paged.Types.Should().ContainSingle().Which.SimpleName.Should().Be("ReservationListItemDto");
        unpaged.Types.Should().ContainSingle().Which.SimpleName.Should().Be("ReservationListItemDto");
        paged.Types.Should().NotContain(t => t.SimpleName == "PagedResult", "the envelope is not an API type");
        paged.Endpoints[0].ResponseType!.IsPaged.Should().BeTrue("the paging flag itself is carried");
    }

    [Fact]
    public void Build_GenericAndArrayResponses_UnwrapToTheElementType()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Showcase.Booking.Dtos
            {
                public class GuestDto { public System.Guid Id { get; set; } }
                public class Envelope<T> { public T? Value { get; set; } }
            }
            """);

        var manifest = Build(
            ImmutableArray.Create(
                Endpoint("A") with { ResponseType = "global::Showcase.Booking.Dtos.Envelope<Showcase.Booking.Dtos.GuestDto>" },
                Endpoint("B") with { ResponseType = "global::Showcase.Booking.Dtos.GuestDto[]" }),
            compilation: compilation);

        manifest.Types.Should().ContainSingle(t => t.SimpleName == "GuestDto");
    }

    [Fact]
    public void Build_EntityMetadata_ProducesEntityTypesTraitsAndBoundaries()
    {
        var entities = ImmutableArray.Create(
            new EntityMetadataModel
            {
                TypeName = "Reservation",
                FullTypeName = "global::Showcase.Booking.Reservations.Reservation",
                Namespace = "Showcase.Booking.Reservations",
                IdType = "System.Guid",
                BoundaryName = "Booking",
                BoundaryTypeFullName = "Showcase.Booking.BookingBoundary",
                IsAuditable = true,
                IsSoftDelete = true,
                Properties = new[]
                {
                    new PropertyMetadataModel { Name = "Code", TypeName = "string", IsRequiredForCreate = true, MaxLength = 32 },
                    new PropertyMetadataModel { Name = "Guest", TypeName = "Guest", IsNavigation = true }
                }.ToEquatableArray()
            },
            new EntityMetadataModel
            {
                TypeName = "AbstractBase",
                FullTypeName = "global::Showcase.Booking.AbstractBase",
                Namespace = "Showcase.Booking",
                IdType = "System.Guid",
                BoundaryName = "Booking",
                IsAbstract = true
            });

        var manifest = Build([Endpoint()], entities);

        var entity = manifest.Types.Should().ContainSingle(t => t.Kind == ManifestTypeKind.Entity).Subject;
        entity.SimpleName.Should().Be("Reservation", "abstract entities are not part of the API surface");
        entity.IdType.Should().Be("System.Guid");
        entity.Traits.Should().NotBeNull();
        entity.Traits!.IsAuditable.Should().BeTrue();
        entity.Traits.IsSoftDelete.Should().BeTrue();
        entity.Properties.Select(p => p.Name).Should().Equal("Code"); // navigations are excluded
        entity.Properties[0].Constraints.MaxLength.Should().Be(32);

        manifest.Boundaries.Should().ContainSingle()
            .Which.Type.Should().Be("Showcase.Booking.BookingBoundary");
    }

    // ---------------------------------------------------------------- actions & permissions

    [Fact]
    public void Build_Actions_ClassifyKindAndDeduplicateByType()
    {
        var mutation = Endpoint("CreateReservation", httpMethod: "Post", route: "") with
        {
            IsMutation = true, MutationEntityType = "global::Showcase.Booking.Reservations.Reservation"
        };
        var voidAction = Endpoint("CancelReservation", httpMethod: "Post", route: "{id}/cancel") with
        {
            IsDomainAction = true, IsVoid = true
        };
        var action = Endpoint("QuoteReservation", httpMethod: "Post", route: "quote") with
        {
            IsDomainAction = true, DomainActionReturnType = "global::Showcase.Booking.Dtos.QuoteDto"
        };

        var manifest = Build(ImmutableArray.Create(mutation, voidAction, action, mutation));

        // Ordered by type name, not by declaration: the manifest is a published document and its
        // catalogues are sorted so a file moving between folders does not rewrite them. Still Equal
        // rather than an order-free assertion — the order is now a rule, and a rule worth keeping is
        // worth failing on.
        manifest.Actions.Select(a => (a.SimpleName, a.Kind)).Should().Equal(
            ("CancelReservation", "voidAction"),
            ("CreateReservation", "mutation"),
            ("QuoteReservation", "action"));
        manifest.Actions.Should().OnlyContain(a => a.Boundary == "Booking");
        // By name rather than by index: which action carries the entity type is the claim, and a
        // position is only a proxy for it — one that broke the moment the catalogue gained an order.
        manifest.Actions.Single(a => a.SimpleName == "CreateReservation")
            .EntityType.Should().Be("global::Showcase.Booking.Reservations.Reservation");
        manifest.Actions.Single(a => a.SimpleName == "QuoteReservation")
            .ReturnType.Should().Be("global::Showcase.Booking.Dtos.QuoteDto");
    }

    [Fact]
    public void Build_Permissions_UnionRequiredAndAnyAcrossEndpointsWithoutDuplicates()
    {
        var a = Endpoint("A") with
        {
            Authorization = new AuthorizationModel
            {
                RequiredPermissions = new[] { "booking.read" }.ToEquatableArray(),
                AnyPermissions = new[] { "booking.admin" }.ToEquatableArray()
            }
        };
        var b = Endpoint("B") with
        {
            Authorization = new AuthorizationModel
            {
                RequiredPermissions = new[] { "booking.read", "booking.write" }.ToEquatableArray()
            }
        };

        var manifest = Build(ImmutableArray.Create(a, b));

        manifest.Permissions.Select(p => p.Name).Should()
            .BeEquivalentTo("booking.read", "booking.admin", "booking.write");
        manifest.Permissions.Should().OnlyContain(p => p.Source == "endpoint");
    }

    // A permission the auto-derivation switch produced is on no attribute, so nothing the manifest can
    // read off an endpoint symbol carries it. Actions contributes it keyed by the operation type; only
    // operations that are actually exposed reach the manifest, and the label says which way it got there.
    [Fact]
    public void Build_DerivedPermissions_AreListedWithTheirSourceAndOnlyWhenExposed()
    {
        var exposed = Endpoint("IssueRefundAction") with { IsDomainAction = true };
        var internalOnly = "global::Showcase.Booking.Reservations.Endpoints.NotAnEndpoint";

        var manifest = ManifestTransform.Build(
            "Showcase.Booking",
            ImmutableArray.Create(exposed),
            ImmutableArray<EntityMetadataModel>.Empty,
            compilation: null,
            contributedTypes: default,
            derivedPermissions: ImmutableArray.Create(
                new SourceGenerator.Core.DerivedPermissionEntry(
                    "Showcase.Booking.Reservations.Endpoints.IssueRefundAction", "booking.issuerefund", "auto-derived"),
                new SourceGenerator.Core.DerivedPermissionEntry(
                    "Showcase.Booking.Reservations.Endpoints.IssueRefundAction", "booking.refund.custom", "explicit"),
                new SourceGenerator.Core.DerivedPermissionEntry(
                    internalOnly.Replace("global::", ""), "booking.hidden", "auto-derived")));

        manifest.Permissions.Select(p => p.Name).Should()
            .BeEquivalentTo("booking.issuerefund", "booking.refund.custom");
        manifest.Permissions.Single(p => p.Name == "booking.issuerefund").Source.Should().Be("auto-derived");
        manifest.Permissions.Single(p => p.Name == "booking.refund.custom").Source.Should().Be("explicit");
    }

    [Fact]
    public void Build_NoDerivedPermissions_LeavesTheEndpointListUntouched()
    {
        var endpoint = Endpoint("A") with
        {
            Authorization = new AuthorizationModel
            {
                RequiredPermissions = new[] { "booking.read" }.ToEquatableArray()
            }
        };

        var manifest = Build(ImmutableArray.Create(endpoint));

        manifest.Permissions.Select(p => p.Name).Should().BeEquivalentTo("booking.read");
        manifest.Permissions.Should().OnlyContain(p => p.Source == "endpoint");
    }

    // The global permission list already carried derived permissions; the endpoint that requires one did
    // not. The manifest is what the OpenAPI document is built from, so an endpoint enforcing a derived
    // permission published a contract that understated what a caller needs.
    [Fact]
    public void Build_DerivedPermission_IsAlsoListedAgainstTheEndpointThatRequiresIt()
    {
        var endpoint = Endpoint("IssueRefundAction") with { IsDomainAction = true };

        var manifest = BuildWithDerived(
            endpoint,
            ("Showcase.Booking.Reservations.Endpoints.IssueRefundAction", "booking.issuerefund"));

        manifest.Endpoints.Single().Authorization!.RequiredPermissions.Should()
            .BeEquivalentTo("booking.issuerefund");
    }

    // Two contributions for one operation: keying to a single value would drop one, and the endpoint
    // would publish a requirement narrower than the one it enforces.
    [Fact]
    public void Build_TwoDerivedPermissionsForOneOperation_BothReachTheEndpoint()
    {
        var endpoint = Endpoint("IssueRefundAction") with { IsDomainAction = true };
        const string fqn = "Showcase.Booking.Reservations.Endpoints.IssueRefundAction";

        var manifest = BuildWithDerived(endpoint, (fqn, "booking.issuerefund"), (fqn, "booking.refund.custom"));

        manifest.Endpoints.Single().Authorization!.RequiredPermissions.Should()
            .BeEquivalentTo("booking.issuerefund", "booking.refund.custom");
    }

    // Auto-derivation skips an operation that already carries a requirement, so an overlap means the same
    // permission arrived by both routes. Listing it twice would describe a requirement that does not exist.
    [Fact]
    public void Build_DerivedPermissionTheEndpointAlreadyDeclares_IsNotListedTwice()
    {
        var endpoint = Endpoint("IssueRefundAction") with
        {
            IsDomainAction = true,
            Authorization = new AuthorizationModel
            {
                RequiredPermissions = new[] { "booking.issuerefund" }.ToEquatableArray()
            }
        };

        var manifest = BuildWithDerived(
            endpoint,
            ("Showcase.Booking.Reservations.Endpoints.IssueRefundAction", "booking.issuerefund"));

        manifest.Endpoints.Single().Authorization!.RequiredPermissions.Should()
            .BeEquivalentTo("booking.issuerefund");
    }

    // An endpoint whose operation has no derived permission must be described exactly as before: the
    // switch is off by default, and turning it on elsewhere cannot rewrite an unrelated contract.
    [Fact]
    public void Build_OperationWithoutADerivedPermission_KeepsItsAuthorizationUntouched()
    {
        var withAuth = Endpoint("A") with
        {
            Authorization = new AuthorizationModel { RequiredPermissions = new[] { "booking.read" }.ToEquatableArray() }
        };
        var withoutAuth = Endpoint("B", route: "b");

        var manifest = BuildWithDerived(
            withAuth, withoutAuth,
            derived: ("Showcase.Booking.Reservations.Endpoints.SomethingElse", "booking.somethingelse"));

        manifest.Endpoints.Single(e => e.OperationId.EndsWith("A")).Authorization!.RequiredPermissions
            .Should().BeEquivalentTo("booking.read");
        manifest.Endpoints.Single(e => e.OperationId.EndsWith("B")).Authorization.Should().BeNull();
    }

    private static ManifestModel BuildWithDerived(
        EndpointModel endpoint, params (string OperationFqn, string Name)[] derived)
        => BuildWithDerived([endpoint], derived);

    private static ManifestModel BuildWithDerived(
        EndpointModel first, EndpointModel second, (string OperationFqn, string Name) derived)
        => BuildWithDerived([first, second], [derived]);

    private static ManifestModel BuildWithDerived(
        ImmutableArray<EndpointModel> endpoints, (string OperationFqn, string Name)[] derived)
        => ManifestTransform.Build(
            "Showcase.Booking",
            endpoints,
            ImmutableArray<EntityMetadataModel>.Empty,
            compilation: null,
            contributedTypes: default,
            derivedPermissions: derived
                .Select(d => new SourceGenerator.Core.DerivedPermissionEntry(d.OperationFqn, d.Name, "auto-derived"))
                .ToImmutableArray());

    // ---------------------------------------------------------------- serialized document

    [Fact]
    public void ManifestJson_IsValidJsonInBothTheConstantAndTheAssemblyAttribute()
    {
        var compilation = ManifestTestHarness.Compile("""
            namespace Showcase.Booking.Dtos
            {
                public enum Status { Draft, Confirmed }
                public class ReservationDto
                {
                    public System.Guid Id { get; set; }
                    public string Label { get; set; } = "";
                    public Status Status { get; set; }
                }
            }
            """);

        var endpoint = Endpoint() with
        {
            Summary = "Reads a \"reservation\"",
            ResponseType = "global::Showcase.Booking.Dtos.ReservationDto",
            Tags = new[] { "booking" }.ToEquatableArray(),
            BodyProperties = new[] { new BodyPropertyModel { Name = "Note", TypeName = "string" } }.ToEquatableArray(),
            Authorization = new AuthorizationModel { RequiredPermissions = new[] { "booking.read" }.ToEquatableArray() }
        };

        var manifest = Build([endpoint], compilation: compilation);
        var source = new ManifestJsonTemplate(manifest).RenderOutput().Text;

        var indented = ManifestTestHarness.ConstantValue(source, "Json");
        var compact = ManifestTestHarness.AssemblyAttributeJson(source);

        JsonValidator.IsValid(indented).Should().BeTrue();
        JsonValidator.IsValid(compact).Should().BeTrue();

        using var doc = JsonDocument.Parse(indented);
        doc.RootElement.GetProperty("$schema").GetString().Should().Be("pragmatic-manifest/v1");
        doc.RootElement.GetProperty("assembly").GetString().Should().Be("Showcase.Booking");
        doc.RootElement.GetProperty("endpoints").GetArrayLength().Should().Be(1);
        doc.RootElement.GetProperty("permissions")[0].GetProperty("name").GetString().Should().Be("booking.read");

        // Both copies must describe the same thing — the host only ever reads the attribute one.
        using var compactDoc = JsonDocument.Parse(compact);
        compactDoc.RootElement.GetProperty("endpoints").GetArrayLength().Should().Be(1);
        compactDoc.RootElement.GetProperty("types").GetArrayLength()
            .Should().Be(doc.RootElement.GetProperty("types").GetArrayLength());
    }

    [Fact]
    public void ManifestJson_HostileSummaryDoesNotBreakTheRawStringLiteral()
    {
        // A summary containing a triple quote would terminate the """ literal early; the manifest is
        // built from user-authored XML docs, so this is representable input.
        var endpoint = Endpoint() with { Summary = "contains \"\"\" three quotes and a \\ backslash" };

        var source = new ManifestJsonTemplate(Build([endpoint])).RenderOutput().Text;

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        tree.GetDiagnostics().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated manifest file must at least parse");
    }

    [Fact]
    public void ManifestJson_NoEndpointsAndNoTypes_ProducesNoArtifact()
    {
        var empty = new ManifestModel { Assembly = "Showcase.Booking", SchemaVersion = "1.0.0" };

        // Validate() renders empty content rather than nothing; SourceOutput.AddSource skips it.
        new ManifestJsonTemplate(empty).RenderOutput().IsEmpty.Should().BeTrue();
    }
}
