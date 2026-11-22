using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A paged query that declares no <c>[Sort]</c> still pages in an order: the entity's key (#938).
/// </summary>
/// <remarks>
///     <para>
///         Without one, <c>Skip</c>/<c>Take</c> are not stable — two requests for page 2 can return different rows,
///         and a row can land on two pages or on none — and EF Core says so with
///         <c>RowLimitingOperationWithoutOrderByWarning</c> on every compiled query.
///     </para>
///     <para>
///         Read on the SQL sent, not on the rows: without an order PostgreSQL usually returns rows in the order
///         they were written, so the response alone cannot tell an ordered page from a lucky one.
///     </para>
/// </remarks>
public class APagedQueryWithoutASort(PostgresFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task ThePageIsReadInTheOrderOfTheKey()
    {
        var capture = new PageReads();

        await using var factory = new ConformanceWebFactory(Fixture.ConnectionString,
            services => services.AddSingleton<IInterceptor>(capture));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/categories/paged?page=1&pageSize=5");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        capture.Commands.Should().NotBeEmpty("the page is read with a LIMIT");
        foreach (var command in capture.Commands)
            command.Should().Contain("ORDER BY", $"a page is read in an order: {command}");
    }

    /// <summary>The statements that read a page of categories, as their text.</summary>
    private sealed class PageReads : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => [.. _commands];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Categories\"", StringComparison.Ordinal)
                && command.CommandText.Contains("LIMIT", StringComparison.Ordinal))
                _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
