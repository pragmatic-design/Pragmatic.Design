using System.Globalization;
using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Persistence.Benchmarks.Workload;
using Pragmatic.Persistence.Query.Adapters;
using PersistenceQuery = Pragmatic.Persistence.Query.Query;

namespace Pragmatic.Persistence.Benchmarks;

/// <summary>
///     A grid's search box: each query filters with a search text nobody typed before, the value placed in the
///     expression tree as a constant against the PrimeNG adapter, which makes it a parameter (#138).
/// </summary>
/// <remarks>
///     <para>
///         EF Core inlines a constant into the SQL and into its compiled-query cache key, so every distinct value
///         compiles a query of its own; a parameter compiles one query for all of them. The constant row is the
///         filter the adapters built before #192 (<c>Expression.Constant</c> as the operand of
///         <c>string.Contains</c>), written out here because the adapters no longer build it.
///     </para>
///     <para>
///         Every invocation takes the next number as its text, so no two queries in a run share a value: the
///         cost measured is that of a value the cache has not seen, which is what a search box sends. A new
///         context per query, as a request has. <see cref="Verify" /> counts the compilations.
///     </para>
/// </remarks>
[MemoryDiagnoser]
public class GridFilterBenchmarks
{
    private const int Tickets = 100;

    private static readonly System.Reflection.MethodInfo StringContains =
        ((MethodCallExpression)((Expression<Func<string, string, bool>>)((s, v) => s.Contains(v))).Body).Method;

    private SqliteConnection _connection = null!;
    private DbContextOptions<TicketDbContext> _options = null!;
    private int _next;

    [GlobalSetup]
    public void Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = OnTheConnection().Options;
        Seed();
    }

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark(Baseline = true)]
    public int Search_ValueAsConstant() => SearchAsConstant(_options, NextSearchText());

    [Benchmark]
    public int Search_PrimeNGAdapter() => SearchThroughTheAdapter(_options, NextSearchText());

    /// <summary>
    ///     Runs <paramref name="distinctValues" /> distinct search texts each way with a compilation counter
    ///     attached, prints how many queries EF compiled, and fails unless the adapter compiled one.
    /// </summary>
    public void Verify(int distinctValues)
    {
        var asConstant = CompilationsOf(distinctValues, SearchAsConstant);
        var throughTheAdapter = CompilationsOf(distinctValues, SearchThroughTheAdapter);

        Console.WriteLine($"{distinctValues} distinct search texts: as a constant {asConstant} compiled queries, "
                          + $"through the PrimeNG adapter {throughTheAdapter}.");

        if (throughTheAdapter != 1)
            throw new InvalidOperationException(
                $"The adapter compiled {throughTheAdapter} queries for {distinctValues} values; one was expected.");
        if (asConstant != distinctValues)
            throw new InvalidOperationException(
                $"The constant filter compiled {asConstant} queries for {distinctValues} values; the baseline "
                + "no longer shows the cost it stands for.");
    }

    // Its own options, so its own model and compiled-query cache: the count starts from an empty cache.
    private int CompilationsOf(int distinctValues, Func<DbContextOptions<TicketDbContext>, string, int> search)
    {
        var compilations = 0;
        var options = OnTheConnection()
            .LogTo(_ => compilations++, [CoreEventId.QueryCompilationStarting])
            .Options;

        for (var i = 0; i < distinctValues; i++)
            search(options, NextSearchText());

        return compilations;
    }

    private static int SearchAsConstant(DbContextOptions<TicketDbContext> options, string text)
    {
        var ticket = Expression.Parameter(typeof(Ticket), "t");
        var title = Expression.Property(ticket, nameof(Ticket.Title));
        var filter = Expression.Lambda<Func<Ticket, bool>>(
            Expression.Call(title, StringContains, Expression.Constant(text)), ticket);

        using var context = new TicketDbContext(options);
        return context.Tickets.Where(filter).Count();
    }

    private static int SearchThroughTheAdapter(DbContextOptions<TicketDbContext> options, string text)
    {
        using var context = new TicketDbContext(options);
        return PersistenceQuery.For<Ticket>()
            .FromPrimeNG(new PrimeNGLazyLoadEvent
            {
                Filters = new Dictionary<string, PrimeNGFilterMetadata>
                {
                    [nameof(Ticket.Title)] = new() { Value = text, MatchMode = "contains" },
                },
            })
            .Build(context.Tickets)
            .Count();
    }

    private string NextSearchText() => (++_next).ToString(CultureInfo.InvariantCulture);

    private DbContextOptionsBuilder<TicketDbContext> OnTheConnection()
        => new DbContextOptionsBuilder<TicketDbContext>().UseSqlite(_connection);

    private void Seed()
    {
        var project = new Project { Id = Guid.NewGuid() };
        using var context = new TicketDbContext(_options);
        context.Database.EnsureCreated();
        context.Projects.Add(project);
        for (var i = 0; i < Tickets; i++)
        {
            context.Tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Title = $"Ticket {i}",
                CreatedAt = DateTimeOffset.UnixEpoch,
                TenantId = ResolvedTenant.Id,
            });
        }

        context.SaveChanges();
    }
}
