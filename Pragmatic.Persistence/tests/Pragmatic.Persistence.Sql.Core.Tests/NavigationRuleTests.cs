using Pragmatic.Persistence.Sql.Core.Tests.ContextRules;
using Pragmatic.Persistence.Sql.Core.Tests.SoftDeletes;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.NavigationRules;

/// <summary>A note on a dossier: its rows are not ruled, the dossier's are.</summary>
[Entity]
[Table("DossierNotes")]
public sealed class DossierNote
{
    public Guid Id { get; set; }
    public Guid DossierId { get; set; }
    public string Text { get; set; } = "";
    public Dossier? Dossier { get; set; }
}

/// <summary>A note with its dossier's title, flattened through the navigation.</summary>
[MapFrom<DossierNote>]
public sealed class NoteRow
{
    public string Text { get; set; } = "";
    public string DossierTitle { get; set; } = "";
}

/// <summary>
///     The target's rules on a to-one navigation, as EF Core applies its query filters there: the join carries them (and a [SoftDelete] target's
///     live rows), so a required navigation to a row the caller cannot see drops the row that reads it; the row's own reads, which join nothing,
///     keep it. Compiled and on the runtime path, in a predicate and in an Include. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class NavigationRuleTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_navigation_reads_the_targets_visible_rows(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "nav-" + Guid.NewGuid().ToString("N")[..6];
        var mine = new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "anna", Title = mark + "-anna", Scopes = [] };
        var theirs = new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "bruno", Title = mark + "-bruno", Scopes = [] };
        await session.InsertManyAsync(new[] { mine, theirs });
        await session.InsertManyAsync(new[]
        {
            new DossierNote { Id = Guid.NewGuid(), DossierId = mine.Id, Text = mark + "-a1" },
            new DossierNote { Id = Guid.NewGuid(), DossierId = theirs.Id, Text = mark + "-b1" },
        });

        using (SqlContext.Use(new SqlContext("acme", "anna")))
        {
            // Through the navigation: only the notes of the dossiers anna sees.
            var compiled = await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Dossier!.Title, m), mark + "%").ToListAsync();
            Assert.Equal([mark + "-a1"], compiled.Select(n => n.Text));
            IQueryable<DossierNote> runtime = session.Query<DossierNote>();
            Assert.Equal([mark + "-a1"], (await runtime.Where(n => n.Dossier!.Title.StartsWith(mark)).ToListAsync()).Select(n => n.Text));

            // An Include of the required navigation: the note whose dossier is hidden goes with it.
            var included = await session.Query<DossierNote>().Include(n => n.Dossier).Where((n, m) => Sql.Like(n.Text, m), mark + "%").ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], included.Select(n => (n.Text, n.Dossier!.Title)));
            var composed = await runtime.Include(n => n.Dossier).Where(n => n.Text.StartsWith(mark)).ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], composed.Select(n => (n.Text, n.Dossier!.Title)));

            // A declared DTO's flattened member: the same join, the same rules.
            var rows = await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Text, m), mark + "%").Project<NoteRow>().ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], rows.Select(r => (r.Text, r.DossierTitle)));
            var composedRows = await runtime.Where(n => n.Text.StartsWith(mark)).Project<DossierNote, NoteRow>().ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], composedRows.Select(r => (r.Text, r.DossierTitle)));

            // The note's own rows, joining nothing: both.
            Assert.Equal(2, (await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Text, m), mark + "%").ToListAsync()).Count);

            // Past the filters: the join carries none.
            Assert.Equal(2, (await session.Query<DossierNote>().WithoutFilters().Where((n, m) => Sql.Like(n.Dossier!.Title, m), mark + "%").ToListAsync()).Count);
        }

        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_rules_of_both_sides_of_a_join_hold(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var mark = "root-" + Guid.NewGuid().ToString("N")[..6];
        var mine = new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "anna", Title = mark + "-anna", Scopes = [] };
        var theirs = new Dossier { Id = Guid.NewGuid(), TenantId = "acme", OwnerId = "bruno", Title = mark + "-bruno", Scopes = [] };
        await session.InsertManyAsync(new[] { mine, theirs });
        await session.InsertManyAsync(new[]
        {
            new DossierNote { Id = Guid.NewGuid(), DossierId = mine.Id, Text = mark + "-a1" },
            new DossierNote { Id = Guid.NewGuid(), DossierId = theirs.Id, Text = mark + "-b1" },
        });

        using (SqlContext.Use(new SqlContext("acme", "anna")))
        {
            // The root's rules filter the root's rows, whatever row shape the joins make of them: the WHERE, on the root's alias.
            var joined = await session.Query<Dossier>().Where((d, m) => Sql.Like(d.Title, m), mark + "%")
                .Join(session.Query<DossierNote>(), d => d.Id, n => n.DossierId, (d, n) => new { d.Title, n.Text }).ToListAsync();
            Assert.Equal([(mark + "-anna", mark + "-a1")], joined.Select(x => (x.Title, x.Text)));
            var left = await session.Query<Dossier>().Where((d, m) => Sql.Like(d.Title, m), mark + "%")
                .LeftJoin(session.Query<DossierNote>(), d => d.Id, n => n.DossierId, (d, n) => new { Dossier = d, Note = n }).ToListAsync();
            Assert.Equal([mark + "-a1"], left.Select(x => x.Note!.Text));
            var lateral = await session.Query<Dossier>().Where((d, m) => Sql.Like(d.Title, m), mark + "%")
                .SelectMany(d => session.Query<DossierNote>().Where(n => n.DossierId == d.Id).OrderBy(n => n.Text).Take(1), (d, n) => new { d.Title, n.Text }).ToListAsync();
            Assert.Equal([mark + "-a1"], lateral.Select(x => x.Text));

            // The inner side's rules, as on its own: an explicit join's in the ON clause, a correlated chain's inside it.
            var inner = await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Text, m), mark + "%")
                .Join(session.Query<Dossier>(), n => n.DossierId, d => d.Id, (n, d) => new { n.Text, d.Title }).ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], inner.Select(x => (x.Text, x.Title)));
            var correlated = await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Text, m), mark + "%")
                .SelectMany(n => session.Query<Dossier>().Where(d => d.Id == n.DossierId).OrderBy(d => d.Title).Take(1), (n, d) => new { n.Text, d.Title }).ToListAsync();
            Assert.Equal([(mark + "-a1", mark + "-anna")], correlated.Select(x => (x.Text, x.Title)));
            var past = await session.Query<DossierNote>().Where((n, m) => Sql.Like(n.Text, m), mark + "%")
                .SelectMany(n => session.Query<Dossier>().WithoutFilters().Where(d => d.Id == n.DossierId).OrderBy(d => d.Title).Take(1), (n, d) => new { n.Text, d.Title }).ToListAsync();
            Assert.Equal(2, past.Count);
        }

        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_navigation_to_a_soft_deleted_row_reads_nothing(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var name = "nav-" + Guid.NewGuid().ToString("N")[..6];
        var venue = new Venue { Id = Guid.NewGuid(), Name = name, IsNew = true };
        venue.AddHall(new Hall { Id = Guid.NewGuid(), Name = "Uno", Seats = 5, IsNew = true });
        await session.SaveGraphAsync(venue);
        Assert.Single(await session.Query<Hall>().Where((h, n) => h.Venue!.Name == n, name).ToListAsync());

        await session.DeleteAsync(venue); // the venue alone: its hall stays live
        Assert.Empty(await session.Query<Hall>().Where((h, n) => h.Venue!.Name == n, name).ToListAsync());
        IQueryable<Hall> runtime = session.Query<Hall>();
        Assert.Empty(await runtime.Where(h => h.Venue!.Name == name).ToListAsync());
        Assert.Empty(await session.Query<Hall>().Include(h => h.Venue).Where((h, id) => h.VenueId == id, venue.Id).ToListAsync());
        Assert.Single(await session.Query<Hall>().Where((h, id) => h.VenueId == id, venue.Id).ToListAsync());

        // A navigation inside a subquery: its join in the subquery's FROM carries the target's live rows too (IncludeDeleted reads the
        // root's deleted rows, not its navigations'; WithoutFilters reads past both).
        Assert.Empty(await session.Query<Venue>().IncludeDeleted().Where((v, n) => v.Name == n && v.Halls.Any(h => h.Venue!.Name == n), name).ToListAsync());
        Assert.Single(await session.Query<Venue>().WithoutFilters().Where((v, n) => v.Name == n && v.Halls.Any(h => h.Venue!.Name == n), name).ToListAsync());

        // An explicit join's inner chain: its live rows on its own alias, in the ON clause.
        Assert.Empty(await session.Query<Hall>().Where((h, id) => h.VenueId == id, venue.Id).Join(session.Query<Venue>(), h => h.VenueId, v => v.Id, (h, v) => new { h.Name, Venue = v.Name }).ToListAsync());
        Assert.Single(await session.Query<Hall>().Where((h, id) => h.VenueId == id, venue.Id).Join(session.Query<Venue>().IncludeDeleted(), h => h.VenueId, v => v.Id, (h, v) => new { h.Name, Venue = v.Name }).ToListAsync());
        transaction.Rollback();
    }
}
