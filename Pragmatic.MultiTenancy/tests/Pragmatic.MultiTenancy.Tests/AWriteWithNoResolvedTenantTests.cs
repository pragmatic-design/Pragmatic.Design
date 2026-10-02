using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Interceptors;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests;

/// <summary>
///     With <c>RequireTenant</c> on, a write of an <see cref="ITenantEntity" /> with no
///     resolved tenant is refused where the read filter refuses, instead of landing on the shared
///     database as a row belonging to nobody.
/// </summary>
/// <remarks>
///     <para>
///         The query filter has always been fail-closed on reads. The write path was not, and a consumer
///         is all writes: a message that arrived with no tenant restored into its scope was written
///         anyway — <c>TenantConnectionInterceptor</c> found no tenant and left the connection on the
///         <b>shared</b> database, and <c>TenantInterceptor</c> stamped nothing. The row was invisible
///         afterwards to that service's own API, whose reads <em>are</em> fail-closed. Measured on
///         Casework: no error, no dead letter, no log.
///     </para>
///     <para>
///         ⚠️ <b>Why it was invisible</b>: a shared-schema application never notices, because the write
///         goes where every other write goes and an empty tenant column is the only trace. It takes
///         db-per-tenant, a consumer, and a test that reads the <em>other</em> tenant's database.
///     </para>
///     <para>
///         <c>RequireTenant</c> is the setting that decides, and its own documentation already said so —
///         "an unresolved tenant causes <b>failure</b> (and tenant queries return no rows…)". The read
///         half was implemented and the failure half was not, so this honours what the option says
///         rather than widening it. An application that deliberately writes without a tenant sets it to
///         <c>false</c>, which the control below pins.
///     </para>
/// </remarks>
public sealed class AWriteWithNoResolvedTenantTests
{
    /// <summary>The setpoint: refused, and the message says what to do about it.</summary>
    [Fact]
    public async Task WithRequireTenant_AndNoResolvedTenant_TheWriteIsRefused()
    {
        await using var context = ContextFor(resolvedTenant: null, requireTenant: true);
        context.Cases.Add(new Case { Id = Guid.NewGuid(), Subject = "A licence for a food stall" });

        var refusal = await Assert.ThrowsAsync<TenantNotResolvedException>(
            () => context.SaveChangesAsync());

        refusal.Message.Should().Contain("Case",
            "the message names the entity whose row would have belonged to nobody");
        refusal.Message.Should().Contain("RequireTenant",
            "and the setting that decides, because an application that means it has one line to change");
    }

    /// <summary>
    ///     The control that keeps this from being a new refusal nobody asked for: with
    ///     <c>RequireTenant = false</c> the write happens exactly as it did before.
    /// </summary>
    [Fact]
    public async Task WithRequireTenantOff_AndNoResolvedTenant_TheWriteHappens()
    {
        await using var context = ContextFor(resolvedTenant: null, requireTenant: false);
        context.Cases.Add(new Case { Id = Guid.NewGuid(), Subject = "A licence for a food stall" });

        await context.SaveChangesAsync();

        context.Cases.Should().ContainSingle().Which.TenantId.Should().BeEmpty(
            "which is the old behaviour, and the reason the option exists: an application that runs "
            + "without a tenant says so");
    }

    /// <summary>
    ///     And the control that matters most: a resolved tenant writes, and the row is stamped. A
    ///     refusal that fired on the normal path would be worse than the defect.
    /// </summary>
    [Fact]
    public async Task WithAResolvedTenant_TheWriteHappens_AndTheRowIsStamped()
    {
        await using var context = ContextFor(resolvedTenant: "acme", requireTenant: true);
        context.Cases.Add(new Case { Id = Guid.NewGuid(), Subject = "A licence for a food stall" });

        await context.SaveChangesAsync();

        context.Cases.Should().ContainSingle().Which.TenantId.Should().Be("acme");
    }

    /// <summary>
    ///     An entity that is not an <see cref="ITenantEntity" /> is nobody's business: it writes with no
    ///     tenant whatever the option says.
    /// </summary>
    [Fact]
    public async Task AnEntityThatBelongsToNoTenant_IsNotRefused()
    {
        await using var context = ContextFor(resolvedTenant: null, requireTenant: true);
        context.Settings.Add(new Setting { Id = Guid.NewGuid(), Value = "42" });

        await context.SaveChangesAsync();

        context.Settings.Should().ContainSingle();
    }

    /// <summary>
    ///     A context on an in-memory SQLite database, which is what keeps this suite hermetic — it is
    ///     the one the gate runs on every story. The provider is not the subject: a SaveChanges
    ///     interceptor runs before any SQL reaches it.
    /// </summary>
    private static TestContext ContextFor(string? resolvedTenant, bool requireTenant)
    {
        var tenantContext = new StubTenantContext(resolvedTenant);

        // Kept open for the context's lifetime: an in-memory SQLite database exists only while a
        // connection to it does, and the context disposes of it with itself.
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();

        var context = new TestContext(new DbContextOptionsBuilder<TestContext>()
            .UseSqlite(connection)
            .AddInterceptors(new TenantInterceptor(tenantContext, requireTenant: requireTenant))
            .Options);

        context.Database.EnsureCreated();
        return context;
    }

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId => tenantId;
        public string? TenantName => tenantId;
        public bool IsResolved => !string.IsNullOrEmpty(tenantId);
    }

    private sealed class TestContext(DbContextOptions<TestContext> options) : DbContext(options)
    {
        public DbSet<Case> Cases => Set<Case>();
        public DbSet<Setting> Settings => Set<Setting>();
    }

    private sealed class Case : ITenantEntity
    {
        public Guid Id { get; set; }
        public string Subject { get; set; } = "";
        public string TenantId { get; set; } = "";
    }

    private sealed class Setting
    {
        public Guid Id { get; set; }
        public string Value { get; set; } = "";
    }
}
