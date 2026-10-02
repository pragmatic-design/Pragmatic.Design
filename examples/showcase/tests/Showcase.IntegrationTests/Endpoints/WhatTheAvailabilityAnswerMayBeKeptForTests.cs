using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>[ResponseCache]</c>: the one read in the Showcase whose answer may be kept, and
///     for how long.
/// </summary>
/// <remarks>
///     <para>
///         <c>SearchAvailableRoomsEndpoint</c> is <c>[AllowAnonymous]</c> and its answer is a
///         function of the query string alone, which is what makes a cache directive safe on it. The
///         declaration is <c>Location.Client</c>: a browser re-searching the same dates may reuse
///         its own answer for fifteen seconds, and no shared cache holds it.
///     </para>
///     <para>
///         ⚠️ The shared form — <c>Location.Any</c>, the default — generates an ASP.NET
///         <c>CacheOutput</c> policy, which does nothing without <c>app.UseOutputCache()</c> in the
///         host. No example wires it, so declaring the default here would have produced a directive
///         with no effect and a test that could only assert the absence of a header.
///     </para>
/// </remarks>
public class WhatTheAvailabilityAnswerMayBeKeptForTests(PostgresFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ThePublicSearch_SaysHowLongItsAnswerMayBeKept()
    {
        var response = await Client.GetAsync(AvailabilityUrl());

        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue(
            "the answer is public data, but keeping it in a shared cache is a decision this "
            + "application has not made — Location.Client keeps it in the caller's own");
        response.Headers.CacheControl.MaxAge.Should().Be(TimeSpan.FromSeconds(15));
    }

    /// <summary>
    ///     The control: a read that declares nothing says nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the availability search carries a Cache-Control" is satisfied by a pipeline
    ///     that puts one on every response — which is a different, and much worse, thing to be true.
    ///     The invoice search is authenticated and declares no cache, and carries none.
    /// </remarks>
    [Fact]
    public async Task AReadThatDeclaresNoCache_CarriesNoDirective()
    {
        var response = await Client.GetAsync($"/api/invoices/search?reservationId={Guid.NewGuid()}");

        response.EnsureSuccessStatusCode();
        response.Headers.CacheControl?.MaxAge.Should().BeNull(
            "nothing declares a cache on this read, and an authenticated one must not acquire one "
            + "by accident");
    }

    private static string AvailabilityUrl()
    {
        var checkIn = DateTimeOffset.UtcNow.AddDays(40).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var checkOut = DateTimeOffset.UtcNow.AddDays(42).ToString("yyyy-MM-ddTHH:mm:ssZ");

        return $"/api/availability?propertyId={Guid.NewGuid()}&checkIn={checkIn}&checkOut={checkOut}&guests=1";
    }
}
