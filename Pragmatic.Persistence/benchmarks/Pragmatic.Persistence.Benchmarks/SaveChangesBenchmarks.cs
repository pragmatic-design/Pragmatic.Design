using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence.Benchmarks.Workload;

namespace Pragmatic.Persistence.Benchmarks;

/// <summary>
///     One <c>SaveChanges</c> of <see cref="Rows" /> modified rows on SQLite in memory: EF Core alone against
///     the interceptors a generated host registers (#137).
/// </summary>
/// <remarks>
///     Each interceptor reads the tracker through <c>ChangeTracker.Entries()</c>, and each such call detects
///     changes over every tracked entity, so the chain's cost grows with the rows times the interceptors.
///     <see cref="Verify" /> counts the passes each row makes.
/// </remarks>
[MemoryDiagnoser]
public class SaveChangesBenchmarks
{
    [Params(10, 100, 1000)]
    public int Rows { get; set; }

    private SqliteConnection _connection = null!;
    private DbContextOptions<TicketDbContext> _efCoreOnly = null!;
    private DbContextOptions<TicketDbContext> _framework = null!;
    private Ticket[] _tickets = [];
    private bool _flip;

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _efCoreOnly = OnTheConnection().Options;
        _framework = OnTheConnection().AddInterceptors(FrameworkInterceptors.AsAHostRegistersThem()).Options;
        _tickets = Seed();
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public int Save_EfCoreOnly() => SaveAll(_efCoreOnly);

    [Benchmark]
    public int Save_FrameworkInterceptors() => SaveAll(_framework);

    /// <summary>
    ///     Saves both rows once with a pass counter attached, and fails unless each wrote every row and the
    ///     interceptors ran. Prints the passes each row makes per save.
    /// </summary>
    public void Verify()
    {
        var efCore = CountPasses(OnTheConnection());
        var framework = CountPasses(OnTheConnection().AddInterceptors(FrameworkInterceptors.AsAHostRegistersThem()));

        using (var context = new TicketDbContext(_efCoreOnly))
        {
            var stamped = context.Tickets.Count(t => t.UpdatedBy == "bench-user");
            if (stamped != Rows)
                throw new InvalidOperationException($"The interceptors stamped {stamped} of {Rows} rows.");
        }

        Console.WriteLine($"{Rows} rows: EF Core alone {efCore} change-detection pass(es) per save, "
                          + $"with the framework's interceptors {framework}.");
    }

    private int CountPasses(DbContextOptionsBuilder<TicketDbContext> options)
    {
        var counter = new DetectChangesCounter();
        var saved = SaveAll(options.LogTo(counter.Log, [CoreEventId.DetectChangesStarting]).Options);
        if (saved != Rows)
            throw new InvalidOperationException($"The save wrote {saved} rows, not {Rows}.");
        return counter.Passes;
    }

    // A new context per save, as a request has: the same instances are attached unchanged, then every
    // title flips, so each save writes every row and allocates the same.
    private int SaveAll(DbContextOptions<TicketDbContext> options)
    {
        using var context = new TicketDbContext(options);
        context.Tickets.AttachRange(_tickets);
        _flip = !_flip;
        var title = _flip ? "Open" : "Closed";
        foreach (var ticket in _tickets)
            ticket.Title = title;
        return context.SaveChanges();
    }

    private DbContextOptionsBuilder<TicketDbContext> OnTheConnection()
        => new DbContextOptionsBuilder<TicketDbContext>().UseSqlite(_connection);

    private Ticket[] Seed()
    {
        var project = new Project { Id = Guid.NewGuid() };
        var tickets = new Ticket[Rows];
        for (var i = 0; i < tickets.Length; i++)
        {
            tickets[i] = new Ticket
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Title = "New",
                Description = $"Ticket {i}",
                Amount = 10m + i,
                Priority = i % 5,
                CreatedAt = DateTimeOffset.UnixEpoch,
                TenantId = ResolvedTenant.Id,
            };
        }

        using var context = new TicketDbContext(_efCoreOnly);
        context.Database.EnsureCreated();
        context.Projects.Add(project);
        context.Tickets.AddRange(tickets);
        context.SaveChanges();
        return tickets;
    }
}
