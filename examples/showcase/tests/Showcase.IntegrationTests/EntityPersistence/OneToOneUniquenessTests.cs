using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Pragmatic.Migrations.Introspection;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     One-to-one means one in the database, not only in the model EF holds.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Relation.OneToOne&lt;Guest&gt;]</c> on <c>GuestPreferences</c> generates
///         <c>HasOne().WithOne().HasForeignKey&lt;GuestPreferences&gt;(e =&gt; e.GuestId)</c>, and EF's own
///         index convention would make that FK index unique. But the table is not built from EF's
///         model: <c>Pragmatic.Migrations</c> builds it from the generated schema constant, and the
///         schema emitted the same plain index a many-to-one gets. So a second preferences row for the
///         same guest went in — measured going in, on a consumer application, before it was measured
///         here.
///     </para>
///     <para>
///         The fixture applies the generated schema through the real diff-and-apply pipeline, which is
///         what makes this measure the divergence rather than EF's convention.
///     </para>
/// </remarks>
public class OneToOneUniquenessTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ASecondDependentForTheSamePrincipal_IsRefusedByTheDatabase()
    {
        var guestId = await CreateGuestAsync();

        await InsertPreferencesAsync(guestId);

        var second = await Record.ExceptionAsync(() => InsertPreferencesAsync(guestId));

        second.Should().NotBeNull(
            "the dependent's foreign key is unique, so the second row has nowhere to go");
        second.Should().BeOfType<PostgresException>();
        ((PostgresException)second!).SqlState.Should().Be(
            PostgresErrorCodes.UniqueViolation,
            "and the database is the one refusing, not the change tracker");
    }

    /// <summary>The control: the index the refusal comes from is in the migrated schema, and unique.</summary>
    /// <remarks>
    ///     Read through the introspector, from the live database, so it says what the migration actually
    ///     created — not what the generator meant.
    /// </remarks>
    [Fact]
    public async Task TheDependentsKey_CarriesAUniqueIndexInTheMigratedSchema()
    {
        await using var connection = new NpgsqlConnection(Fixture.AppConnectionString);
        await connection.OpenAsync();

        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(connection);

        var table = schema.Tables.FirstOrDefault(t => t.Name == "GuestPreferenceses");
        table.Should().NotBeNull("the dependent side of the one-to-one has its own table");

        table!.Indexes.Should().Contain(
            i => i.Columns.Length == 1 && i.Columns[0] == "GuestId" && i.IsUnique,
            "one-to-one");

        // ⚠️ The control on the other cardinality: a many-to-one key is indexed and NOT unique. Without
        // it a generator that marked every foreign-key index unique would pass the assertion above and
        // break every collection.
        var reservations = schema.Tables.FirstOrDefault(t => t.Name == "Reservations");
        reservations.Should().NotBeNull();
        reservations!.Indexes.Should().Contain(
            i => i.Columns.Length == 1 && i.Columns[0] == "GuestId" && !i.IsUnique,
            "a guest has many reservations");
    }

    private async Task<Guid> CreateGuestAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "OneToOne",
            lastName = "Uniqueness",
            email = $"o2o.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();
    }

    /// <summary>
    ///     Inserts a preferences row with plain SQL.
    /// </summary>
    /// <remarks>
    ///     Not through EF: a second context, a raw insert or two concurrent transactions are exactly the
    ///     writers a model-only constraint does not reach, and the point is what the table permits.
    /// </remarks>
    private async Task InsertPreferencesAsync(Guid guestId)
    {
        await using var connection = new NpgsqlConnection(Fixture.AppConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "GuestPreferenceses"
                ("PersistenceId", "GuestId", "PrefersHighFloor", "PrefersQuietRoom")
            VALUES (@id, @guestId, false, false)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("guestId", guestId);

        await command.ExecuteNonQueryAsync();
    }
}
