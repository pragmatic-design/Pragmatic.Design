using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence.EFCore.Sequences;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Sequences;

/// <summary>
///     A <c>[GeneratedValue] {SEQ:N}</c> value is taken from the database the connection was
///     <b>routed</b> to, not from the one the DbContext was registered with.
/// </summary>
/// <remarks>
///     <para>
///         The case that produced this test is db-per-tenant: <c>TenantConnectionInterceptor</c> rewrites
///         the connection string on open, so every write of a request goes to that organisation's own
///         database. A sequence read that opens the connection itself never reaches that interceptor, and
///         the number is then drawn from the shared database — measured in the Casework example, where
///         <c>Case_Number_seq</c> existed only there while two organisations had databases of their own.
///     </para>
///     <para>
///         ⚠️ The visible symptom was not a wrong number: it was <c>23505</c> on
///         <c>pg_class_relname_nsp_index</c>, three concurrent requests creating one sequence in one
///         database. A number drawn from the wrong database is the defect; the duplicate key was how it
///         announced itself.
///     </para>
///     <para>
///         The interceptor here is the smallest thing shaped like the real one — it rewrites the
///         connection string to a second database and nothing else. SQLite because that keeps the test
///         hermetic; what is being measured is which connection the provider uses, which is not
///         provider-specific.
///     </para>
/// </remarks>
public class ASequenceIsCreatedWhereTheConnectionWasRoutedTests
{
    private sealed class ProbeContext(DbContextOptions options) : DbContext(options);

    /// <summary>Rewrites the connection string on open, the way db-per-tenant does.</summary>
    private sealed class RouteTo(string connectionString) : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            connection.ConnectionString = connectionString;
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            connection.ConnectionString = connectionString;
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task NextAsync_WhenTheConnectionIsRoutedElsewhere_CreatesTheSequenceThere()
    {
        var registered = $"DataSource=registered-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var routed = $"DataSource=routed-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        // A shared-cache in-memory database lives as long as one connection to it is open.
        using var keepRegistered = new SqliteConnection(registered);
        using var keepRouted = new SqliteConnection(routed);
        keepRegistered.Open();
        keepRouted.Open();

        var options = new DbContextOptionsBuilder()
            .UseSqlite(registered)
            .AddInterceptors(new RouteTo(routed))
            .Options;

        using var db = new ProbeContext(options);

        (await SequenceValueProvider.NextAsync(db, "probe_seq")).Should().Be(1);

        (await HasSequenceTableAsync(keepRouted)).Should().BeTrue(
            "the value was drawn from the database the connection was routed to");
        (await HasSequenceTableAsync(keepRegistered)).Should().BeFalse(
            "and not from the one the DbContext was registered with, which is the shared database");
    }

    private static async Task<bool> HasSequenceTableAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "select count(*) from sqlite_master where type = 'table' and name = '__pragmatic_sequences'";

        return Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false)) > 0;
    }
}
