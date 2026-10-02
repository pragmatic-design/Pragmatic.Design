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
///     Two <c>[LoadEntity]</c> of one entity on a real database: one query for both keys, each field taken from it,
///     a key that names nothing a 404 naming it.
/// </summary>
/// <remarks>
///     Through <c>NameLabelPairAction</c>, which answers the names of the two labels it was given — the
///     query count is read on the SQL sent, as <see cref="TheRowsNamedByAList" /> reads it.
/// </remarks>
public class TheRowsOfOneEntityNamedByTwoKeys(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private const string Route = "/api/labels/pair";

    private async Task<Guid> ALabelAsync(string name)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();

        var label = Label.Create();
        db.Add(label);
        db.Entry(label).Property(nameof(Label.Name)).CurrentValue = name;
        await db.SaveChangesAsync();

        return label.PersistenceId;
    }

    [Fact]
    public async Task TwoKeysOfOneEntity_AreReadInOneQuery()
    {
        var urgent = await ALabelAsync("urgent");
        var fragile = await ALabelAsync("fragile");
        var capture = new LabelReads();

        await using var factory = new ConformanceWebFactory(Fixture.ConnectionString,
            services => services.AddSingleton<IInterceptor>(capture));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Route, JsonContent(new { firstId = fragile, secondId = urgent }));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(body);
        JsonDocument.Parse(body).RootElement.EnumerateArray().Select(n => n.GetString())
            .Should().Equal(["fragile", "urgent"], "each field is the row its own key names");
        capture.Commands.Should().HaveCount(1, $"one query for both keys, not one per load: {string.Join(" | ", capture.Commands)}");
    }

    [Fact]
    public async Task ASecondKeyThatNamesNothing_Is404_NamingIt()
    {
        var urgent = await ALabelAsync("urgent");
        var nothing = Guid.NewGuid();

        var response = await PostAsync(Route, new { firstId = urgent, secondId = nothing });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, body);
        JsonDocument.Parse(body).RootElement.GetProperty("entityId").GetString()
            .Should().Be(nothing.ToString(), "the key that names nothing, not the one that does");
    }

    /// <summary>The control: an optional key that is null is not asked, and the other is still read.</summary>
    [Fact]
    public async Task ANullOptionalKey_IsNotAsked()
    {
        var urgent = await ALabelAsync("urgent");

        var names = await ReadAsync(await PostAsync(Route, new { firstId = urgent }));

        names.EnumerateArray().Select(n => n.GetString()).Should().Equal(["urgent"]);
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
