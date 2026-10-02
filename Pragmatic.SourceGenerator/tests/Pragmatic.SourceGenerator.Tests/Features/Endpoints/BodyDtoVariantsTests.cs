using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Endpoints;

/// <summary>
///     Which request-body records an endpoint generates, and what they are called.
/// </summary>
/// <remarks>
///     <para>
///         Three places need this answer: the two templates that emit the records, and the AOT JSON
///         context that has to cover them. The third derived it independently and assumed
///         <c>{Type}Body</c>, which a versioned endpoint never emits — the generated context named a type
///         that does not exist and the Showcase stopped compiling.
///     </para>
///     <para>
///         Tested against a constructed model: the naming rule is the thing that broke, and driving the
///         whole endpoint pipeline to reach it would need Asp.Versioning referenced by this harness.
///         That the two templates now read the same list is covered by the Endpoints suite and the
///         Showcase build.
///     </para>
/// </remarks>
public class BodyDtoVariantsTests
{
    private static BodyPropertyModel Property(string name, string? sinceVersion = null)
        => new() { Name = name, TypeName = "string", SinceVersion = sinceVersion };

    private static EndpointModel Endpoint(
        ImmutableArray<BodyPropertyModel> body,
        ImmutableArray<ActionVersionModel> versions = default,
        bool hasAspVersioning = false)
        => new()
        {
            Namespace = "App.Orders",
            TypeName = "PlaceOrderAction",
            FullTypeName = "global::App.Orders.PlaceOrderAction",
            Accessibility = "public",
            HttpMethod = "POST",
            Route = "/orders",
            IsVoid = false,
            IsDomainAction = true,
            BodyProperties = body,
            ActionVersions = versions.IsDefault ? ImmutableArray<ActionVersionModel>.Empty : versions,
            HasAspVersioning = hasAspVersioning,
        };

    private static ActionVersionModel Version(int major, params BodyPropertyModel[] body)
        => new()
        {
            Major = major,
            Minor = 0,
            Patch = 0,
            MethodName = major == 1 ? "Execute" : $"ExecuteV{major}",
            BodyProperties = [..body],
        };

    [Fact]
    public void AnUnversionedEndpoint_HasOneVariantNamedAfterTheAction()
    {
        var variants = Endpoint([Property("Name"), Property("Phone")]).BodyDtoVariants;

        variants.Should().ContainSingle();
        variants[0].Name.Should().Be("PlaceOrderActionBody");
        variants[0].ApiVersion.Should().BeNull();
        variants[0].Properties.Select(p => p.Name).Should().Equal("Name", "Phone");
    }

    [Fact]
    public void AVersionedEndpoint_HasOnePerVersion_AndNeverThePlainName()
    {
        var name = Property("Name");
        var phone = Property("Phone", sinceVersion: "2.0");

        var variants = Endpoint(
            [name, phone],
            [Version(1, name), Version(2, name, phone)],
            hasAspVersioning: true).BodyDtoVariants;

        variants.Select(v => v.Name).Should().Equal("PlaceOrderActionV1Body", "PlaceOrderActionV2Body");
        variants.Should().NotContain(v => v.Name == "PlaceOrderActionBody",
            "the unversioned record is not emitted for a versioned endpoint — naming it produces code that does not compile");
        variants[0].Properties.Select(p => p.Name).Should().Equal("Name");
        variants[1].Properties.Select(p => p.Name).Should().Equal("Name", "Phone");
    }

    [Fact]
    public void VersionedWithoutAspVersioning_FallsBackToTheSingleRecord()
    {
        // Without the Asp.Versioning runtime the generator emits the unversioned handler and body, and
        // says so with PRAG0551. The variant list has to follow what is actually emitted, not what the
        // attributes asked for.
        var name = Property("Name");
        var phone = Property("Phone", sinceVersion: "2.0");

        var variants = Endpoint([name, phone], [Version(1, name), Version(2, name, phone)]).BodyDtoVariants;

        variants.Should().ContainSingle();
        variants[0].Name.Should().Be("PlaceOrderActionBody");
    }

    [Fact]
    public void AnEndpointWithNoBody_HasNoVariants()
    {
        Endpoint([]).BodyDtoVariants.Should().BeEmpty();
    }
}
