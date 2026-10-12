using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     A filter built at run time from a caller's value compiles one query, whatever the value (#138).
/// </summary>
/// <remarks>
///     <para>
///         The grid adapters and the hierarchy filters put the value into the expression tree. As a constant,
///         EF Core inlines it into the SQL and into its compiled-query cache key, so each distinct search text
///         compiled a query of its own. As a parameter, the second value finds the first one's compiled query.
///     </para>
///     <para>
///         Each case runs the same filter twice with different values and counts the compilations of the
///         second run (<c>CoreEventId.QueryCompilationStarting</c>): the first run fills the cache, so the second
///         compiles nothing when the value is a parameter, and once when it is a constant.
///     </para>
/// </remarks>
public sealed class ARuntimeFilterCompilesOnceTests : IDisposable
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherRoot = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private int _compilations;
    private readonly GridDbContext _db;

    public ARuntimeFilterCompilesOnceTests()
    {
        var options = new DbContextOptionsBuilder<GridDbContext>()
            .UseSqlite("DataSource=:memory:")
            .LogTo(_ => Interlocked.Increment(ref _compilations), [CoreEventId.QueryCompilationStarting])
            .Options;

        _db = new GridDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        _db.Nodes.AddRange(
            new Node { Id = Root }, new Node { Id = Child, ParentId = Root }, new Node { Id = OtherRoot });
        _db.Units.AddRange(
            new Unit { Id = "eng" }, new Unit { Id = "backend", ParentId = "eng" }, new Unit { Id = "sales" });
        _db.Members.AddRange(
            new Member { Id = 1, Name = "Alice", NodeId = Root, UnitId = "eng" },
            new Member { Id = 2, Name = "Bob", NodeId = Child, UnitId = "backend" },
            new Member { Id = 3, Name = "Carol", NodeId = OtherRoot, UnitId = "sales" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void PrimeNG_TwoSearchTexts_CompileOnce()
    {
        PrimeNG("Ali").Should().Equal("Alice");

        var compiled = CompilationsOf(() => PrimeNG("Bo").Should().Equal("Bob"));

        compiled.Should().Be(0, "the search text is a parameter of one compiled query");
    }

    [Fact]
    public void DevExpress_TwoValues_CompileOnce()
    {
        DevExpress("Alice").Should().Equal("Alice");

        var compiled = CompilationsOf(() => DevExpress("Carol").Should().Equal("Carol"));

        compiled.Should().Be(0, "the compared value is a parameter of one compiled query");
    }

    [Fact]
    public void WhereInDirectChildren_TwoRoots_CompileOnce()
    {
        DirectChildren(Root).Should().Equal("Alice", "Bob");

        var compiled = CompilationsOf(() => DirectChildren(OtherRoot).Should().Equal("Carol"));

        compiled.Should().Be(0, "the root is a parameter of one compiled query");
    }

    [Fact]
    public void WhereInSubtree_TwoSubtrees_CompileOnce()
    {
        Subtree("eng").Should().Equal("Alice", "Bob");

        var compiled = CompilationsOf(() => Subtree("sales").Should().Equal("Carol"));

        compiled.Should().Be(0, "the subtree's ids are one parameter of one compiled query");
    }

    private int CompilationsOf(Action run)
    {
        var before = _compilations;
        run();
        return _compilations - before;
    }

    private List<string> PrimeNG(string search)
        => PersistenceQuery.For<Member>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    [nameof(Member.Name)] = new() { Value = search, MatchMode = "contains" },
                },
            })
            .Build(_db.Members)
            .OrderBy(m => m.Id)
            .Select(m => m.Name)
            .ToList();

    private List<string> DevExpress(string name)
        => PersistenceQuery.For<Member>()
            .FromDevExpress(new DevExpressLoadOptions { Filter = new List<object?> { nameof(Member.Name), "=", name } })
            .Build(_db.Members)
            .OrderBy(m => m.Id)
            .Select(m => m.Name)
            .ToList();

    private List<string> DirectChildren(Guid root)
        => HierarchyFilter.WhereInDirectChildren<Member, Node, Guid>(
                _db.Members, _db.Nodes, n => n.Id, n => n.ParentId, m => m.NodeId, root)
            .OrderBy(m => m.Id)
            .Select(m => m.Name)
            .ToList();

    private List<string> Subtree(string root)
        => _db.Members
            .WhereInSubtree(_db, root, m => m.UnitId, "Units")
            .OrderBy(m => m.Id)
            .Select(m => m.Name)
            .ToList();

    private sealed class Node
    {
        public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }

    private sealed class Unit
    {
        public string Id { get; set; } = "";
        public string? ParentId { get; set; }
    }

    private sealed class Member
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public Guid NodeId { get; set; }
        public string UnitId { get; set; } = "";
    }

    private sealed class GridDbContext(DbContextOptions<GridDbContext> options) : DbContext(options)
    {
        public DbSet<Node> Nodes => Set<Node>();
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<Member> Members => Set<Member>();
    }
}
