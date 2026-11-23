using Invoicing.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.Responses;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     What <see cref="WritersObserved" /> measures belongs to the host it was given. The assembly runs another host
///     beside this suite's (the generated contract tests'), and the counter it reads is one for the whole process.
/// </summary>
public sealed class TheWritersOneHostAnsweredWith(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task AResponseOfAnotherHost_IsNotCountedForThisOne()
    {
        await using var other = await AnotherHostAsync();
        using var client = other.GetTestClient();

        using var ours = new WritersObserved(Services);
        using var theirs = new WritersObserved(other.Services);
        var response = await client.GetAsync("/value");
        response.EnsureSuccessStatusCode();

        // The control: the other host's response was measured, so this one's silence is a filter, not an absence.
        theirs.Seen.Should().Equal("serializer");
        ours.Seen.Should().BeEmpty();
    }

    /// <summary>
    ///     A host that answers through a generated response, with options no entry point marked: the serializer writes
    ///     it, and the counter says so.
    /// </summary>
    private static async Task<WebApplication> AnotherHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();

        var app = builder.Build();
        app.MapGet("/value", () => new GeneratedJsonResponse<string>(
            "value", 200, null, static (writer, value, _) => writer.WriteStringValue(value),
            new GeneratedJsonShape([typeof(string)], [], [], needsInfrastructureExclusion: false)));
        await app.StartAsync();
        return app;
    }
}
