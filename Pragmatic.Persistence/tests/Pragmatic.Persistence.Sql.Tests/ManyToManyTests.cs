using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A many-to-many side (<c>[ManyToMany("ProjectLabels", "ProjectId", "LabelId")]</c>): the collection in lambdas (Any, All, Count,
///     SelectMany) through the join table, Include loading it on the parents' keys, the links written one at a time (LinkAsync /
///     UnlinkAsync, the write batch) or synced by a graph save from the tracked list, and dropped with the parent by a graph delete.
/// </summary>
[Collection("databases")]
public class ManyToManyTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void The_collection_renders_through_the_join_table()
    {
        var session = Session(SqlDialect.PostgreSql);
        Assert.EndsWith("WHERE (EXISTS (SELECT 1 FROM \"Labels\" c1 INNER JOIN \"ProjectLabels\" c1_l ON c1_l.\"LabelId\" = c1.\"Id\" WHERE c1_l.\"ProjectId\" = t.\"Id\" AND (c1.\"Name\" = @p0)))", session.Query<Project>().Where(p => p.Labels.Any(l => l.Name == "urgent")).ToSql(), StringComparison.Ordinal);
        Assert.EndsWith("WHERE ((SELECT COUNT(*) FROM \"Labels\" c1 INNER JOIN \"ProjectLabels\" c1_l ON c1_l.\"LabelId\" = c1.\"Id\" WHERE c1_l.\"ProjectId\" = t.\"Id\") > @p0)", session.Query<Project>().Where(p => p.Labels.Count > 1).ToSql(), StringComparison.Ordinal);
        Assert.Contains("INNER JOIN \"ProjectLabels\" j1_l ON j1_l.\"ProjectId\" = t.\"Id\" INNER JOIN \"Labels\" j1 ON j1.\"Id\" = j1_l.\"LabelId\"", session.Query<Project>().SelectMany(p => p.Labels, (p, l) => new { p.Name, Label = l.Name }).ToSql(), StringComparison.Ordinal);

        // The include's companion statement: the labels joined to the link, filtered on the link's parent column, which comes back last.
        var include = session.Query<Project>().Where(p => p.Name != "").Include(p => p.Labels).ToSql();
        Assert.Contains("SELECT t.\"Id\", t.\"Name\", t_l.\"ProjectId\" FROM \"Labels\" t INNER JOIN \"ProjectLabels\" t_l ON t_l.\"LabelId\" = t.\"Id\" WHERE (t_l.\"ProjectId\" IN (SELECT t.\"Id\" FROM \"Projects\" t WHERE (t.\"Name\" <> @p0)))", include, StringComparison.Ordinal);

        var collection = SqlEntityMap<Project>.Current.Collection(nameof(Project.Labels))!;
        Assert.True(collection.IsLink);
        Assert.Equal("ProjectLabels", collection.LinkTable);
        Assert.Equal("ProjectId", collection.ForeignKeyColumn);
        Assert.Equal("LabelId", collection.LinkChildColumn);
        Assert.False(SqlEntityMap<Project>.Current.Collection(nameof(Project.Tickets))!.IsLink);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Links_are_written_read_synced_and_dropped_with_the_parent(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var labels = new[] { "urgent", "billing", "ops" }.Select(n => new Label { Id = Guid.NewGuid(), Name = n }).ToList();
        var projects = new[] { "alpha", "beta", "gamma" }.Select(n => new Project { Id = Guid.NewGuid(), Name = n, IsNew = true }).ToList();
        try
        {
            await session.InsertManyAsync(labels);
            await session.InsertManyAsync(projects);
            foreach (var project in projects)
            {
                project.IsNew = false;
                project.ResetModifiedProperties();
            }

            // One link at a time, and a batch of them.
            await session.LinkAsync(projects[0], nameof(Project.Labels), labels[0]);
            await session.WriteBatch().Link(projects[0], nameof(Project.Labels), labels[1]).Link(projects[1], nameof(Project.Labels), labels[1]).ExecuteAsync();
            var duplicate = await Assert.ThrowsAnyAsync<Exception>(() => session.LinkAsync(projects[0], nameof(Project.Labels), labels[0]));
            Assert.NotNull(duplicate);

            // The collection in lambdas: Any, All, Count, a count projected, SelectMany.
            Assert.Equal(["alpha", "beta"], await session.Query<Project>().Where(p => p.Labels.Any(l => l.Name == "billing")).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
            Assert.Equal(["alpha"], await session.Query<Project>().Where(p => p.Labels.Count > 1).Select(p => p.Name).ToListAsync());
            Assert.Equal(["gamma"], await session.Query<Project>().Where(p => !p.Labels.Any()).Select(p => p.Name).ToListAsync());
            Assert.Equal(["beta", "gamma"], await session.Query<Project>().Where(p => p.Labels.All(l => l.Name != "urgent")).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync());
            var counts = await session.Query<Project>().OrderBy(p => p.Name).Select(p => new { p.Name, Labels = p.Labels.Count }).ToListAsync();
            Assert.Equal([("alpha", 2), ("beta", 1), ("gamma", 0)], counts.Select(c => (c.Name, c.Labels)));
            var pairs = await session.Query<Project>().SelectMany(p => p.Labels, (p, l) => new { Project = p.Name, Label = l.Name }).OrderBy(x => x.Project).ThenBy(x => x.Label).ToListAsync();
            Assert.Equal([("alpha", "billing"), ("alpha", "urgent"), ("beta", "billing")], pairs.Select(x => (x.Project, x.Label)));

            // Include loads the labels of every project in one round trip, through the join table (the typed plan and the runtime path).
            var loaded = await session.Query<Project>().OrderBy(p => p.Name).Include(p => p.Labels).ToListAsync();
            Assert.Equal(["billing", "urgent"], loaded[0].Labels.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(["billing"], loaded[1].Labels.Select(l => l.Name));
            Assert.Empty(loaded[2].Labels);
            var dynamic = await session.Query<Project>().AsQueryable().Where(p => p.Labels.Any(l => l.Name == "urgent")).Include(p => p.Labels).ToListAsync();
            Assert.Equal("alpha", Assert.Single(dynamic).Name);
            Assert.Equal(2, dynamic[0].Labels.Count);

            // A graph save syncs the links to the tracked list: beta loses billing, gains urgent and ops; the labels themselves are untouched.
            var beta = loaded[1];
            beta.RemoveLabel(beta.Labels.Single());
            beta.AddLabel(labels[0]);
            beta.AddLabel(labels[2]);
            beta.SetName("beta2");
            Assert.Equal(4, await session.SaveGraphAsync(beta)); // the name, the orphan links, two links
            var betaNow = await session.Query<Project>().Where((p, id) => p.Id == id, beta.Id).Include(p => p.Labels).FirstAsync();
            Assert.Equal("beta2", betaNow.Name);
            Assert.Equal(["ops", "urgent"], betaNow.Labels.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(3, await session.Query<Label>().CountAsync());

            // Unlink, then a graph delete drops the parent's links (and its tickets), not the labels.
            await session.UnlinkAsync(projects[0], nameof(Project.Labels), labels[1]);
            Assert.Equal(["urgent"], (await session.Query<Project>().Where((p, id) => p.Id == id, projects[0].Id).Include(p => p.Labels).FirstAsync()).Labels.Select(l => l.Name));
            await session.DeleteGraphAsync(betaNow);
            Assert.Equal(2, await session.Query<Project>().CountAsync());
            Assert.Equal(1, await session.Query<Project>().Where(p => p.Labels.Any()).CountAsync());
            Assert.Equal(3, await session.Query<Label>().CountAsync());
        }
        finally
        {
            // The remaining projects, their links and the labels: the links through the graph delete, the rest by key list.
            var ids = projects.Select(p => p.Id).ToArray();
            var remaining = await session.Query<Project>().Where((p, keys) => keys.Contains(p.Id), ids).ToListAsync();
            foreach (var project in remaining)
                await session.DeleteGraphAsync(project);
            await session.Query<Label>().Where((l, keys) => keys.Contains(l.Id), labels.Select(l => l.Id).ToArray()).ExecuteDeleteAsync();
        }
    }
}
