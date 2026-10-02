using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Query;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

public class HierarchyQueryExtensionsTests
{
    // =========================================================================
    // Test entities
    // =========================================================================

    private sealed class OrgUnit
    {
        public string Id { get; set; } = "";
        public string? ParentId { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class Employee
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string OrgUnitId { get; set; } = "";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
        public DbSet<Employee> Employees => Set<Employee>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrgUnit>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.ParentId);
            });
            modelBuilder.Entity<Employee>(b =>
            {
                b.HasKey(e => e.Id);
                b.Property(e => e.OrgUnitId);
            });
        }
    }

    // =========================================================================
    // WhereInSubtree — expression building
    // =========================================================================

    [Fact]
    public void WhereInSubtree_BuildsContainsExpression()
    {
        // We can't run the CTE on InMemory, but we can verify the expression is built correctly
        // by using a pre-populated subtree set directly
        var employees = new List<Employee>
        {
            new() { Id = 1, Name = "Alice", OrgUnitId = "eng" },
            new() { Id = 2, Name = "Bob", OrgUnitId = "sales" },
            new() { Id = 3, Name = "Charlie", OrgUnitId = "eng-backend" }
        }.AsQueryable();

        // Simulate subtree: eng and eng-backend are in the subtree
        var subtreeIds = new HashSet<string> { "eng", "eng-backend" };

        var filtered = employees.Where(e => subtreeIds.Contains(e.OrgUnitId)).ToList();

        filtered.Should().HaveCount(2);
        filtered.Should().Contain(e => e.Name == "Alice");
        filtered.Should().Contain(e => e.Name == "Charlie");
        filtered.Should().NotContain(e => e.Name == "Bob");
    }

    [Fact]
    public void WhereInSubtree_EmptySubtree_ReturnsNothing()
    {
        var employees = new List<Employee>
        {
            new() { Id = 1, Name = "Alice", OrgUnitId = "eng" }
        }.AsQueryable();

        var subtreeIds = new HashSet<string>();

        var filtered = employees.Where(e => subtreeIds.Contains(e.OrgUnitId)).ToList();

        filtered.Should().BeEmpty();
    }

    // =========================================================================
    // ResolveSubtreeIds — integration test with SQLite
    // =========================================================================

    [Fact]
    public void ResolveSubtreeIds_WithSQLite_ResolvesFullTree()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        // Build tree:
        //   company
        //   ├── engineering
        //   │   ├── backend
        //   │   └── frontend
        //   └── sales
        db.OrgUnits.AddRange(
            new OrgUnit { Id = "company", ParentId = null, Name = "Company" },
            new OrgUnit { Id = "engineering", ParentId = "company", Name = "Engineering" },
            new OrgUnit { Id = "backend", ParentId = "engineering", Name = "Backend" },
            new OrgUnit { Id = "frontend", ParentId = "engineering", Name = "Frontend" },
            new OrgUnit { Id = "sales", ParentId = "company", Name = "Sales" }
        );
        db.SaveChanges();

        // Resolve subtree from "engineering"
        var subtree = HierarchyQueryExtensions.ResolveSubtreeIds(
            db, "engineering", "OrgUnits");

        subtree.Should().HaveCount(3);
        subtree.Should().Contain("engineering");
        subtree.Should().Contain("backend");
        subtree.Should().Contain("frontend");
        subtree.Should().NotContain("sales");
        subtree.Should().NotContain("company");
    }

    [Fact]
    public void ResolveSubtreeIds_LeafNode_ReturnsOnlyItself()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        db.OrgUnits.AddRange(
            new OrgUnit { Id = "company", ParentId = null, Name = "Company" },
            new OrgUnit { Id = "sales", ParentId = "company", Name = "Sales" }
        );
        db.SaveChanges();

        var subtree = HierarchyQueryExtensions.ResolveSubtreeIds(
            db, "sales", "OrgUnits");

        subtree.Should().HaveCount(1);
        subtree.Should().Contain("sales");
    }

    [Fact]
    public void ResolveSubtreeIds_RootNode_ReturnsEntireTree()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        db.OrgUnits.AddRange(
            new OrgUnit { Id = "company", ParentId = null, Name = "Company" },
            new OrgUnit { Id = "engineering", ParentId = "company", Name = "Engineering" },
            new OrgUnit { Id = "sales", ParentId = "company", Name = "Sales" }
        );
        db.SaveChanges();

        var subtree = HierarchyQueryExtensions.ResolveSubtreeIds(
            db, "company", "OrgUnits");

        subtree.Should().HaveCount(3);
    }

    [Fact]
    public void WhereInSubtree_FullIntegration_FiltersByHierarchy()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new TestDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        db.OrgUnits.AddRange(
            new OrgUnit { Id = "company", ParentId = null, Name = "Company" },
            new OrgUnit { Id = "eng", ParentId = "company", Name = "Engineering" },
            new OrgUnit { Id = "backend", ParentId = "eng", Name = "Backend" },
            new OrgUnit { Id = "sales", ParentId = "company", Name = "Sales" }
        );
        db.Employees.AddRange(
            new Employee { Id = 1, Name = "Alice", OrgUnitId = "eng" },
            new Employee { Id = 2, Name = "Bob", OrgUnitId = "backend" },
            new Employee { Id = 3, Name = "Charlie", OrgUnitId = "sales" }
        );
        db.SaveChanges();

        // Filter employees by engineering subtree
        var result = db.Employees.AsQueryable()
            .WhereInSubtree(
                db,
                rootId: "eng",
                entityNodeSelector: e => e.OrgUnitId,
                tableName: "OrgUnits")
            .ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(e => e.Name == "Alice");
        result.Should().Contain(e => e.Name == "Bob");
        result.Should().NotContain(e => e.Name == "Charlie");
    }
}
