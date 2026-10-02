using System.Net;
using System.Net.Http.Json;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests;

/// <summary>
///     An optional header or query value on an <c>init</c> property is bound when sent, and leaves the
///     declaration in place when absent.
/// </summary>
/// <remarks>
///     The generated endpoint puts these values in the object initializer as
///     <c>value ?? &lt;declared default&gt;</c>. Compiling proves the shape; this proves what reaches the
///     operation.
/// </remarks>
public sealed class AnOptionalInitValueTests
{
    private static async Task<string?> EchoAsync(string query, string? source)
    {
        await using var factory = new AuthenticatingTestFactory(endpointOptions: null);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/notes/echo{query}");
        request.Headers.Add(NoCredentialsAuthenticationHandler.UserHeader, "someone");
        if (source is not null)
            request.Headers.Add("X-Note-Source", source);

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<string>();
    }

    [Fact]
    public async Task SentValues_ReachTheOperation()
    {
        (await EchoAsync("?limit=7", source: "web")).Should().Be("web|7");
    }

    /// <summary>
    ///     The control: absent values are not bound as anything — the header stays null and the query
    ///     value keeps the 20 it was declared with, not 0.
    /// </summary>
    [Fact]
    public async Task AbsentValues_LeaveTheDeclarationInPlace()
    {
        (await EchoAsync("", source: null)).Should().Be("none|20");
    }
}
