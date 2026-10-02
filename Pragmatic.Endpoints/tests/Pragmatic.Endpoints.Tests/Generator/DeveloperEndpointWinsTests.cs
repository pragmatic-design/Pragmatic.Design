using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A hand-written endpoint on a route the generator would scaffold makes the generator stand down.
/// </summary>
/// <remarks>
///     <para>
///         <c>EndpointsFeature</c> calls it "dev endpoint wins": before emitting handlers for the
///         endpoints that <c>[Resource]</c> and the traits contribute, it drops every one whose verb and
///         route a hand-written <c>[Endpoint]</c> already claims. Registering both would make the route
///         answer 500 per request — ASP.NET Core refuses the request, not the registration.
///     </para>
///     <para>
///         It is also the framework's clearest statement of a general rule: <b>declare it yourself and
///         yours is what ships; declare nothing and Pragmatic writes it.</b> This pins it, because the
///         failure it prevents is invisible in a build.
///     </para>
/// </remarks>
public class DeveloperEndpointWinsTests : EndpointsGeneratorTestBase
{
    private const string GeneratedCreateEndpoint = "_Resource.Guest.Create.Endpoint";

    private static string Source(string handWrittenRoute) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Result;

        namespace TestApp.Booking;

        [Boundary]
        public sealed class BookingBoundary;

        [Entity]
        [BelongsTo<BookingBoundary>]
        [Resource("guests", Capabilities = ResourceCapabilities.Create)]
        public partial class Guest : IEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        [DomainAction]
        [BelongsTo<BookingBoundary>]
        [Endpoint(HttpVerb.Post, "{{handWrittenRoute}}")]
        public partial class AddGuestAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    private static bool EmitsGeneratedCreateEndpoint(string handWrittenRoute)
        => GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source(handWrittenRoute)))
            .Keys.Any(k => k.Contains(GeneratedCreateEndpoint));

    /// <summary>
    ///     Same verb and route: the scaffolded endpoint is not emitted at all.
    /// </summary>
    [Fact]
    public void OnTheSameVerbAndRoute_TheScaffoldedEndpointStandsDown()
    {
        EmitsGeneratedCreateEndpoint("api/booking/guests").Should().BeFalse(
            "both registered means the route answers 500, so the one nobody wrote is the one to drop");
    }

    /// <summary>
    ///     Control: the scaffolding is not disabled wholesale by the presence of a hand-written endpoint.
    /// </summary>
    /// <remarks>
    ///     Without this, a rule that dropped every scaffolded endpoint whenever any manual one existed
    ///     would pass the test above and leave the resource with no CRUD at all.
    /// </remarks>
    [Fact]
    public void OnADifferentRoute_TheScaffoldedEndpointIsStillEmitted()
    {
        EmitsGeneratedCreateEndpoint("api/booking/guests/import").Should().BeTrue(
            "standing down is per route, not per assembly");
    }

    /// <summary>
    ///     Control: the same route under a different verb is two endpoints, which is ordinary REST.
    /// </summary>
    [Fact]
    public void OnTheSameRouteUnderADifferentVerb_TheScaffoldedEndpointIsStillEmitted()
    {
        var source = Source("api/booking/guests").Replace(
            "[Endpoint(HttpVerb.Post, \"api/booking/guests\")]",
            "[Endpoint(HttpVerb.Delete, \"api/booking/guests\")]");

        GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(source))
            .Keys.Any(k => k.Contains(GeneratedCreateEndpoint)).Should().BeTrue(
                "POST and DELETE on one path do not collide");
    }
}
