using System.Linq;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The one collision "dev endpoint wins" does not catch, and PRAG0529 must (PRAG0529).
/// </summary>
/// <remarks>
///     <para>
///         Two checks look at the same thing through different eyes. The stand-down rule in
///         <c>EndpointsFeature</c> compares the route <b>as declared</b>
///         (<c>$"{HttpMethod}:{Route}"</c>); the duplicate check compares the route <b>as served</b>
///         (<c>EndpointRouteFacts.FullRoute</c>, group prefix included). An endpoint inside an
///         <c>[EndpointGroup]</c> declares a short route, so the first sees no overlap and lets the
///         scaffolded endpoint through, while the second sees two endpoints on one path — which is what
///         the application will see too, as a 500 per request.
///     </para>
///     <para>
///         Reporting it needs a line somebody can edit. The scaffolded endpoint has no location, so
///         reporting on <c>ordered.Skip(1)</c> after sorting by type name would fail: whenever the
///         scaffolded one sorts later — <c>Resource…</c> against most hand-written names — it is the
///         one picked, and the guard on a null location then drops the report entirely.
///     </para>
/// </remarks>
public class DuplicateRouteThroughAGroupTests : EndpointsGeneratorTestBase
{
    private const string Source = """
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

        [EndpointGroup("/api/booking", Tag = "Booking")]
        public sealed class BookingGroup;

        [Entity]
        [BelongsTo<BookingBoundary>]
        [Resource("guests", Capabilities = ResourceCapabilities.Create)]
        public partial class Guest : IEntity
        {
            public string Name { get; set; } = string.Empty;
        }

        // Declares "guests"; served at /api/booking/guests, which is where [Resource] puts Create.
        [DomainAction]
        [BelongsTo<BookingBoundary>]
        [Endpoint(HttpVerb.Post, "guests")]
        [EndpointGroup<BookingGroup>]
        public partial class AddGuestAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    /// <summary>
    ///     Both endpoints are emitted, so the collision is real and not hypothetical.
    /// </summary>
    /// <remarks>
    ///     This is the premise of the test below. If the stand-down rule ever learns about group
    ///     prefixes, this assertion fails first and says so, instead of leaving the diagnostic test
    ///     passing for a reason that no longer exists.
    /// </remarks>
    [Fact]
    public void TheScaffoldedEndpointIsEmittedAnyway_BecauseTheDeclaredRoutesDiffer()
    {
        GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(Source))
            .Keys.Any(k => k.Contains("_Resource.Guest.Create.Endpoint")).Should().BeTrue(
                "the stand-down rule compares the declared route, and \"guests\" is not \"/api/booking/guests\"");
    }

    [Fact]
    public void TheCollisionIsReported()
    {
        HasDiagnostic(RunGeneratorWithPersistence(Source), "PRAG0529").Should().BeTrue(
            "two endpoints answer POST /api/booking/guests, and routing cannot choose between them");
    }
}
