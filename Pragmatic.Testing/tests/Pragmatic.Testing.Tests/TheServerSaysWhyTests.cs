using System.Net;
using Pragmatic.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.Tests;

/// <summary>
///     A failed contract carries the server's own explanation, not only its status code.
/// </summary>
/// <remarks>
///     ⚠️ A generated contract is exactly the test whose author is not in the room. A message that names
///     the route and the status and stops there turns a 400 from a validator that says which field is
///     wrong into "returned 400 BadRequest". The body is the only place that answer exists.
/// </remarks>
public class TheServerSaysWhyTests
{
    [Fact]
    public void AFailedSuccessAssertion_CarriesTheResponseBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"errors\":{\"CheckIn\":[\"must be in the future\"]}}"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/reservations")
        };

        var thrown = Assert.Throws<PragmaticTestAssertionException>(() => response.ShouldBeSuccess());

        thrown.Message.Should().Contain("must be in the future",
            "the reason the request was refused is in the body and nowhere else");
        thrown.Message.Should().Contain("400", "and the status still names what happened");
    }

    /// <summary>
    ///     The created entity is identified from the <c>Location</c> header when there is one.
    /// </summary>
    [Fact]
    public async Task WithALocation_TheIdComesFromTheHeader()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Headers = { Location = new Uri("/api/reservations/42", UriKind.Relative) },
            Content = new StringContent("\"99\""),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/reservations")
        };

        (await response.ShouldIdentifyTheCreatedAsync()).Should().Be("42",
            "the header is the address the server itself gave");
    }

    /// <summary>
    ///     And from the body when there is not — which is what a Pragmatic app answers.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>Results.Created((string?)null, value)</c> is what the result-to-HTTP mapping emits for a
    ///     create: 201, the id in the body, and no <c>Location</c>. A contract that could only read the
    ///     header could not follow a create through — so the transition it was arranging never ran, and
    ///     the early return reported that as success.
    /// </remarks>
    [Theory]
    [InlineData("\"01a07740-e0fa-7b53-ae84-8b9d7e02a838\"", "01a07740-e0fa-7b53-ae84-8b9d7e02a838")]
    [InlineData("{\"id\":\"01a07740-e0fa-7b53-ae84-8b9d7e02a838\"}", "01a07740-e0fa-7b53-ae84-8b9d7e02a838")]
    [InlineData("{\"Id\":42}", "42")]
    public async Task WithNoLocation_TheIdComesFromTheBody(string body, string expected)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(body),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/reservations")
        };

        (await response.ShouldIdentifyTheCreatedAsync()).Should().Be(expected);
    }

    /// <summary>
    ///     Neither place: a failure that names both, never a quiet return.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The contract read the header and returned when it was absent, so a create that says nothing
    ///     about what it made turned the transition into a test that asserts nothing and passes — the
    ///     shape of a skipped placeholder, without the skip that makes it visible.
    /// </remarks>
    [Fact]
    public async Task WithNeither_ItFails_NamingBothPlacesItLooked()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"status\":\"ok\"}"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/reservations")
        };

        var thrown = await Assert.ThrowsAsync<PragmaticTestAssertionException>(
            () => response.ShouldIdentifyTheCreatedAsync());

        thrown.Message.Should().Contain("Location");
        thrown.Message.Should().Contain("body");
        thrown.Message.Should().Contain("api/reservations", "the route that did not say what it created");
    }

    /// <summary>The control: a body that is not there adds nothing, and says nothing false.</summary>
    [Fact]
    public void WithNoBody_TheMessageIsTheStatusAlone()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/reservations")
        };

        var thrown = Assert.Throws<PragmaticTestAssertionException>(() => response.ShouldBeSuccess());

        thrown.Message.Should().Contain("400");
        thrown.Message.Should().NotContain("The server said",
            "an empty body is not an explanation");
    }
}
