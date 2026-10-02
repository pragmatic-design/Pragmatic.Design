using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.EFCore.Interceptors;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     Tests for <see cref="TenantInterceptor" />.
///     Verifies TenantId is auto-set on Added entities implementing ITenantEntity.
/// </summary>
public class TenantInterceptorTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class TestTenantEntity : ITenantEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string TenantId { get; set; } = "";
    }

    private sealed class NonTenantEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<TestTenantEntity> TenantEntities { get; set; } = null!;
        public DbSet<NonTenantEntity> NonTenantEntities { get; set; } = null!;
    }

    private sealed class FakeTenantContext(string? tenantId, bool isResolved = true) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
        public string? TenantName => null;
        public bool IsResolved => isResolved;
    }

    private static TestDbContext CreateContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantInterceptor(tenantContext))
            .Options;
        return new TestDbContext(options);
    }

    // =========================================================================
    // Tests
    // =========================================================================

    [Fact]
    public async Task SaveChangesAsync_NewTenantEntity_SetsTenantId()
    {
        var tenantContext = new FakeTenantContext("tenant-a");
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "Test" });
        await context.SaveChangesAsync();

        var entity = await context.TenantEntities.FirstAsync();
        entity.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task SaveChangesAsync_PreSetTenantId_IsOverwrittenToCurrentTenant()
    {
        // Secure-by-default: a caller cannot persist a row for another tenant by pre-setting
        // TenantId — in a resolved context it is overwritten with the current tenant.
        var tenantContext = new FakeTenantContext("tenant-b");
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "Pre-set", TenantId = "tenant-other" });
        await context.SaveChangesAsync();

        var entity = await context.TenantEntities.FirstAsync();
        entity.TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_TenantTransfer_IsReverted()
    {
        // Changing TenantId on update (a tenant transfer) is reverted to the loaded value.
        var tenantContext = new FakeTenantContext("tenant-a");
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "Original", TenantId = "tenant-a" });
        await context.SaveChangesAsync();

        var entity = await context.TenantEntities.FirstAsync();
        entity.TenantId = "tenant-hijack";
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public async Task SaveChangesAsync_UnresolvedTenant_DoesNotSetTenantId()
    {
        var tenantContext = new FakeTenantContext(null, isResolved: false);
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "No tenant" });
        await context.SaveChangesAsync();

        var entity = await context.TenantEntities.FirstAsync();
        entity.TenantId.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveChangesAsync_ModifiedEntity_DoesNotChangeTenantId()
    {
        var tenantContext = new FakeTenantContext("tenant-a");
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "Original", TenantId = "tenant-a" });
        await context.SaveChangesAsync();

        var entity = await context.TenantEntities.FirstAsync();
        entity.Name = "Updated";
        await context.SaveChangesAsync();

        entity.TenantId.Should().Be("tenant-a");
    }

    [Fact]
    public void SavingChanges_Sync_SetsTenantId()
    {
        var tenantContext = new FakeTenantContext("tenant-sync");
        using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "Sync" });
        context.SaveChanges();

        var entity = context.TenantEntities.First();
        entity.TenantId.Should().Be("tenant-sync");
    }

    [Fact]
    public async Task SaveChangesAsync_MultipleEntities_AllGetTenantId()
    {
        var tenantContext = new FakeTenantContext("tenant-multi");
        await using var context = CreateContext(tenantContext);

        context.TenantEntities.Add(new TestTenantEntity { Name = "A" });
        context.TenantEntities.Add(new TestTenantEntity { Name = "B" });
        context.TenantEntities.Add(new TestTenantEntity { Name = "C" });
        await context.SaveChangesAsync();

        var entities = await context.TenantEntities.ToListAsync();
        entities.Should().AllSatisfy(e => e.TenantId.Should().Be("tenant-multi"));
    }

    [Fact]
    public async Task SaveChangesAsync_NonTenantEntity_NotAffected()
    {
        var tenantContext = new FakeTenantContext("tenant-x");
        await using var context = CreateContext(tenantContext);

        context.NonTenantEntities.Add(new NonTenantEntity { Name = "Not tenant" });
        await context.SaveChangesAsync();

        var entity = await context.NonTenantEntities.FirstAsync();
        entity.Name.Should().Be("Not tenant");
    }
}
