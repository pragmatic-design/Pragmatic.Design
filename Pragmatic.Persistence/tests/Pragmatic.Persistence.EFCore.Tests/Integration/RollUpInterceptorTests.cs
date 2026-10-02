using System;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.RollUp;
using Pragmatic.Persistence.RollUp;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Validates the roll-up interceptor (#2): a parent's stored aggregate is kept current as children are
///     created and deleted, in the same unit of work, against a real (SQLite) database.
/// </summary>
public class RollUpInterceptorTests
{
    private sealed class Invoice
    {
        public Guid Id { get; set; }
        public decimal Subtotal { get; private set; }
        public void ApplyDelta(decimal delta) => Subtotal += delta;
    }

    private sealed class Line
    {
        public Guid Id { get; set; }
        public Guid InvoiceId { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class SoftLine : Pragmatic.Persistence.Entity.ISoftDelete
    {
        public Guid Id { get; set; }
        public Guid InvoiceId { get; set; }
        public decimal Amount { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public string? DeletedBy { get; set; }
    }

    private sealed class RollUpDbContext(DbContextOptions<RollUpDbContext> options) : DbContext(options)
    {
        public DbSet<Invoice> Invoices => Set<Invoice>();
        public DbSet<Line> Lines => Set<Line>();
        public DbSet<SoftLine> SoftLines => Set<SoftLine>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Invoice>().HasKey(i => i.Id);
            builder.Entity<Invoice>().Property(i => i.Subtotal).HasPrecision(18, 2);
            builder.Entity<Line>().HasKey(l => l.Id);
            builder.Entity<Line>().Property(l => l.Amount).HasPrecision(18, 2);
            builder.Entity<SoftLine>().HasKey(l => l.Id);
            builder.Entity<SoftLine>().Property(l => l.Amount).HasPrecision(18, 2);
        }
    }

    private static RollUpRule<Line, Invoice> Rule() => new()
    {
        AggregatePropertyName = nameof(Invoice.Subtotal),
        ParentKey = l => l.InvoiceId,
        Amount = l => l.Amount,
        ApplyToParent = (invoice, delta) => invoice.ApplyDelta(delta)
    };

    private static RollUpRule<SoftLine, Invoice> SoftRule() => new()
    {
        AggregatePropertyName = nameof(Invoice.Subtotal),
        ParentKey = l => l.InvoiceId,
        Amount = l => l.Amount,
        ApplyToParent = (invoice, delta) => invoice.ApplyDelta(delta)
    };

    [Fact]
    public async Task RollUp_MaintainsParentAggregate_OnChildCreateAndDelete()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<RollUpDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new RollUpInterceptor([Rule()]))
                .Options;

            var invoiceId = Guid.NewGuid();
            var lineToDeleteId = Guid.NewGuid();

            // Create invoice + two lines in one unit of work → Subtotal = 30.
            using (var ctx = new RollUpDbContext(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Invoices.Add(new Invoice { Id = invoiceId });
                ctx.Lines.Add(new Line { Id = lineToDeleteId, InvoiceId = invoiceId, Amount = 10m });
                ctx.Lines.Add(new Line { Id = Guid.NewGuid(), InvoiceId = invoiceId, Amount = 20m });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
            {
                var invoice = await ctx.Invoices.SingleAsync();
                invoice.Subtotal.Should().Be(30m);
            }

            // Delete one line → Subtotal drops by 10.
            using (var ctx = new RollUpDbContext(options))
            {
                var line = await ctx.Lines.SingleAsync(l => l.Id == lineToDeleteId);
                await ctx.Invoices.SingleAsync(i => i.Id == invoiceId);   // ensure parent is tracked
                ctx.Lines.Remove(line);
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
            {
                var invoice = await ctx.Invoices.SingleAsync();
                invoice.Subtotal.Should().Be(20m);
            }
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public async Task RollUp_AmountModified_AdjustsParentByDelta()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<RollUpDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new RollUpInterceptor([Rule()]))
                .Options;

            var invoiceId = Guid.NewGuid();
            var lineId = Guid.NewGuid();

            using (var ctx = new RollUpDbContext(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Invoices.Add(new Invoice { Id = invoiceId });
                ctx.Lines.Add(new Line { Id = lineId, InvoiceId = invoiceId, Amount = 10m });
                await ctx.SaveChangesAsync();
            }

            // Edit the line's amount 10 → 25: the parent must move by the DELTA (+15), not drift.
            using (var ctx = new RollUpDbContext(options))
            {
                var line = await ctx.Lines.SingleAsync(l => l.Id == lineId);
                line.Amount = 25m;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
                (await ctx.Invoices.SingleAsync()).Subtotal.Should().Be(25m);
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public async Task RollUp_ChildReparented_MovesAmountBetweenParents()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<RollUpDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new RollUpInterceptor([Rule()]))
                .Options;

            var invoiceA = Guid.NewGuid();
            var invoiceB = Guid.NewGuid();
            var lineId = Guid.NewGuid();

            using (var ctx = new RollUpDbContext(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Invoices.Add(new Invoice { Id = invoiceA });
                ctx.Invoices.Add(new Invoice { Id = invoiceB });
                ctx.Lines.Add(new Line { Id = lineId, InvoiceId = invoiceA, Amount = 10m });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
            {
                var line = await ctx.Lines.SingleAsync(l => l.Id == lineId);
                line.InvoiceId = invoiceB;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
            {
                (await ctx.Invoices.SingleAsync(i => i.Id == invoiceA)).Subtotal.Should().Be(0m);
                (await ctx.Invoices.SingleAsync(i => i.Id == invoiceB)).Subtotal.Should().Be(10m);
            }
        }
        finally
        {
            connection.Close();
        }
    }

    [Fact]
    public async Task RollUp_SoftDeleteAndRestore_AdjustsParentAggregate()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<RollUpDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new RollUpInterceptor([SoftRule()]))
                .Options;

            var invoiceId = Guid.NewGuid();
            var lineId = Guid.NewGuid();

            using (var ctx = new RollUpDbContext(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Invoices.Add(new Invoice { Id = invoiceId });
                ctx.SoftLines.Add(new SoftLine { Id = lineId, InvoiceId = invoiceId, Amount = 10m });
                await ctx.SaveChangesAsync();
            }

            // Soft delete: reaches EF as Modified, must decrement the aggregate like a delete.
            using (var ctx = new RollUpDbContext(options))
            {
                var line = await ctx.SoftLines.SingleAsync(l => l.Id == lineId);
                line.IsDeleted = true;
                line.DeletedAt = DateTimeOffset.UtcNow;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
                (await ctx.Invoices.SingleAsync()).Subtotal.Should().Be(0m);

            // Restore: the amount re-joins the aggregate.
            using (var ctx = new RollUpDbContext(options))
            {
                var line = await ctx.SoftLines.SingleAsync(l => l.Id == lineId);
                line.IsDeleted = false;
                line.DeletedAt = null;
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RollUpDbContext(options))
                (await ctx.Invoices.SingleAsync()).Subtotal.Should().Be(10m);
        }
        finally
        {
            connection.Close();
        }
    }
    [Fact]
    public async Task RollUp_StaleTrackedParent_DoesNotLoseConcurrentIncrement()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<RollUpDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(new RollUpInterceptor([Rule()]))
                .Options;

            var invoiceId = Guid.NewGuid();

            using (var setup = new RollUpDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                setup.Invoices.Add(new Invoice { Id = invoiceId });
                await setup.SaveChangesAsync();
            }

            // ctx1 loads the invoice FIRST (tracked copy Subtotal=0, soon stale)...
            using var ctx1 = new RollUpDbContext(options);
            _ = await ctx1.Invoices.SingleAsync(i => i.Id == invoiceId);

            // ...then a concurrent context adds a line → DB Subtotal = 10.
            using (var ctx2 = new RollUpDbContext(options))
            {
                ctx2.Lines.Add(new Line { Id = Guid.NewGuid(), InvoiceId = invoiceId, Amount = 10m });
                await ctx2.SaveChangesAsync();
            }

            // ctx1 now adds a line of 5. The read-modify-write on its STALE tracked copy would
            // compute 0+5=5 and overwrite ctx2's increment (the classic lost update). The
            // relational SET Subtotal = Subtotal + 5 must yield 15 instead.
            ctx1.Lines.Add(new Line { Id = Guid.NewGuid(), InvoiceId = invoiceId, Amount = 5m });
            await ctx1.SaveChangesAsync();

            using (var check = new RollUpDbContext(options))
                (await check.Invoices.SingleAsync()).Subtotal.Should().Be(15m);
        }
        finally
        {
            connection.Close();
        }
    }
}
