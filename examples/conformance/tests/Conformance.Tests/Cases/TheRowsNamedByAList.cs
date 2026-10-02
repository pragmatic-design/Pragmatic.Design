using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Conformance.Sales.Entities;
using Conformance.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     <c>[LoadEntities&lt;T&gt;(nameof(Ids))]</c> on a real database: the rows a list of keys names, in one
///     query, and one 404 naming every key that names nothing.
/// </summary>
/// <remarks>
///     Through <c>NameLabelsAction</c>, which answers the names of the labels it was given — so
///     the order and the duplicates are read on the response, and the query count on the SQL sent. The keys
///     are its only input and not a scalar, so the request body is the list itself, not an object holding it.
/// </remarks>
public class TheRowsNamedByAList(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string Route = "/api/labels/names";

    private async Task<Guid> ALabelAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        // The name has a private setter and is no parameter of the factory: written through the entry,
        // as TheLinkedRows does.
        var label = Label.Create();
        db.Add(label);
        db.Entry(label).Property(nameof(Label.Name)).CurrentValue = name;
        await db.SaveChangesAsync();

        return label.PersistenceId;
    }

    [Fact]
    public async Task TheRows_ComeInTheOrderOfTheKeys_AKeyGivenTwiceOnce()
    {
        var urgent = await ALabelAsync("urgent");
        var fragile = await ALabelAsync("fragile");

        var names = await ReadAsync(await PostAsync(Route, new[] { fragile, urgent, fragile }));

        names.EnumerateArray().Select(n => n.GetString()).Should().Equal("fragile", "urgent");
    }

    [Fact]
    public async Task AKeyThatNamesNothing_Is404_NamingEveryMissingKey()
    {
        var urgent = await ALabelAsync("urgent");
        var nothing = Guid.NewGuid();
        var nothingEither = Guid.NewGuid();

        var response = await PostAsync(Route, new[] { nothing, urgent, nothingEither });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        var entityId = JsonDocument.Parse(body).RootElement.GetProperty("entityId").GetString();
        entityId.Should().Be($"{nothing}, {nothingEither}", "every missing key is named, in the order given, and none that exists");
    }

    /// <summary>The control: every key present is no 404, and the rows are read in one query.</summary>
    [Fact]
    public async Task EveryKeyPresent_IsReadInOneQuery()
    {
        var urgent = await ALabelAsync("urgent");
        var fragile = await ALabelAsync("fragile");
        var capture = new LabelReads();

        await using var factory = new ConformanceWebFactory(Fixture.ConnectionString,
            services => services.AddSingleton<IInterceptor>(capture));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, JsonContent(new[] { urgent, fragile }));
        var body = await response.Content.ReadAsStringAsync();

        // A success, not a 404 — the POST answers 201, as CountOrderLinesAction documents for any POST.
        response.IsSuccessStatusCode.Should().BeTrue(body);
        capture.Commands.Should().HaveCount(1, $"one query for the list, not one per key: {string.Join(" | ", capture.Commands)}");
    }

    [Fact]
    public async Task AnEmptyList_IsNoRows()
    {
        var names = await ReadAsync(await PostAsync(Route, Array.Empty<Guid>()));

        names.GetArrayLength().Should().Be(0);
    }

    private static StringContent JsonContent(object body)
        => new(JsonSerializer.Serialize(body, JsonOptions), System.Text.Encoding.UTF8, "application/json");

    /// <summary>Every query that reads the labels, as its text.</summary>
    private sealed class LabelReads : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> _commands = new();

        public IReadOnlyList<string> Commands => [.. _commands];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"Labels\"", StringComparison.Ordinal))
                _commands.Enqueue(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
