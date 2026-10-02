using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Hierarchy;

/// <summary>
///     Demonstrates the generated <c>[GenerateHierarchy]</c> queries on <see cref="Folder"/>:
///     <c>GetDescendants(rootId)</c> and <c>GetAncestors(childId)</c>, both backed by a recursive
///     CTE emitted in <c>FolderHierarchyExtensions</c> (extension methods on <c>DbContext</c>).
///     Runs on SQLite so the CTE actually executes.
/// </summary>
public static class HierarchySample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Hierarchy ([GenerateHierarchy] → GetDescendantsByParent / GetAncestorsByParent) ═══");
        Console.WriteLine();

        // SQLite in-memory: keep the connection open for the DB's lifetime.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        try
        {
            var options = new DbContextOptionsBuilder<HierarchyDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var db = new HierarchyDbContext(options);
            await db.Database.EnsureCreatedAsync();

            // Build a tree:  Root → Docs → {Specs, Guides → Advanced},  Root → Media
            var root = Folder("Root", null);
            var docs = Folder("Docs", root.Id);
            var specs = Folder("Specs", docs.Id);
            var guides = Folder("Guides", docs.Id);
            var advanced = Folder("Advanced", guides.Id);
            var media = Folder("Media", root.Id);

            db.Folders.AddRange(root, docs, specs, guides, advanced, media);
            await db.SaveChangesAsync();

            Console.WriteLine("  Tree:");
            Console.WriteLine("    Root");
            Console.WriteLine("    ├─ Docs");
            Console.WriteLine("    │  ├─ Specs");
            Console.WriteLine("    │  └─ Guides");
            Console.WriteLine("    │     └─ Advanced");
            Console.WriteLine("    └─ Media");
            Console.WriteLine();

            // ── Descendants of Docs (full subtree, not just direct children) — generated CTE ──
            var descendants = await db.GetDescendantsByParent(docs.Id).ToListAsync();
            Console.WriteLine($"  GetDescendantsByParent(Docs) : {Names(descendants)} (expects Advanced, Guides, Specs)");

            // ── Ancestors of Advanced (walks up to the root) — generated CTE ──
            var ancestors = await db.GetAncestorsByParent(advanced.Id).ToListAsync();
            Console.WriteLine($"  GetAncestorsByParent(Advanced): {Names(ancestors)} (expects Docs, Guides, Root)");
            Console.WriteLine();
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static Folder Folder(string name, Guid? parentId)
    {
        // ParentId is generated from the declared relation, and written through its setter.
        var folder = new Folder { Name = name };
        folder.SetParentId(parentId);
        return folder;
    }

    private static string Names(IEnumerable<Folder> folders)
        => string.Join(", ", folders.Select(f => f.Name).OrderBy(n => n));
}
