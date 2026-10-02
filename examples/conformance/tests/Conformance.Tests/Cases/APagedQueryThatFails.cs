using System.Data.Common;
using System.Net;
using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A paged query whose execution fails answers with the error's status, as ProblemDetails.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The paged branch of the handler goes through <c>ErrorExtensions.ToResult</c> like every other
///         failure of the same handler. A <c>Results.BadRequest(result.Error)</c> there would turn a database
///         that does not answer into a <b>400</b> — «the request was wrong» — with the <c>QueryError</c>
///         serialized as <c>application/json</c>, without <c>type</c>, <c>title</c> or <c>status</c>.
///     </para>
///     <para>
///         The fault is a SQL command that throws, injected as an interceptor: the same road as a database
///         that fails mid-read — the executor catches it and translates it into <c>QueryError.Database</c>,
///         status 500.
///     </para>
/// </remarks>
public class APagedQueryThatFails(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string Route = "/api/orders/guarded?reference=ORD";

    [Fact]
    public async Task ItsExecutionFailing_AnswersTheErrorsStatus_AsAProblem()
    {
        await using var factory = new ConformanceWebFactory(Fixture.ConnectionString,
            services => services.AddSingleton<IInterceptor>(new OrdersReadFails()));
        using var client = Allowed(factory.CreateClient());

        var response = await client.GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            $"a database failure is the server's, not a request the client got wrong: {body}");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        JsonDocument.Parse(body).RootElement.GetProperty("status").GetInt32().Should().Be(500);
        body.Should().NotContain("Simulated database failure",
            "the provider's message stays on the server: the raw QueryError the route used to answer carried it");
    }

    /// <summary>The control: the same route, with a database that answers, gives the page.</summary>
    [Fact]
    public async Task ItsExecutionSucceeding_AnswersThePage()
    {
        var response = await Allowed(Client).GetAsync(Route);

        var page = await ReadAsync(response);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        page.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }

    private static HttpClient Allowed(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-User-Id", "paged-reader");
        client.DefaultRequestHeaders.Add("X-User-Name", "paged-reader");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "conformance.order.guardedread");
        return client;
    }

    /// <summary>Every read of the orders throws, like a database that fails during the query.</summary>
    private sealed class OrdersReadFails : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
            => command.CommandText.Contains("\"Orders\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated database failure while reading the orders.")
                : base.ReaderExecutingAsync(command, eventData, result, cancellationToken);

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
            => command.CommandText.Contains("\"Orders\"", StringComparison.Ordinal)
                ? throw new InvalidOperationException("Simulated database failure while counting the orders.")
                : base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
