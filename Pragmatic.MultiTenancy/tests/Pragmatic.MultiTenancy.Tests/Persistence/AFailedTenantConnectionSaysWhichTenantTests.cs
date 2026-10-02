using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy.Persistence;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Persistence;

/// <summary>
///     A tenant whose dedicated database will not open is named, instead of leaving the
///     caller with the driver's SQL state and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         When the register says the organisation is active and gives a connection string, and nobody
///         has created the database, the driver alone reports
///         <c>Npgsql.PostgresException 3D000: database "casework_tenant_acme" does not exist</c> —
///         a database name and a SQL state, with no way back to the tenant.
///     </para>
///     <para>
///         <b>Hermetic, and say which failure is being asserted.</b> These cases use SQLite and a
///         directory that does not exist, so the failure is <i>the connection did not open</i> rather
///         than <i>the database does not exist</i>. That is deliberate: the sentence under test is
///         provider-agnostic — it names the tenant and points at provisioning — and the provider's own
///         wording, whichever it is, survives as the inner exception. ⚠️ The faithful 3D000 against a
///         reachable PostgreSQL server is <b>not</b> asserted here; it needs a container.
///     </para>
/// </remarks>
public class AFailedTenantConnectionSaysWhichTenantTests
{
    /// <summary>A path no test run creates, so the open fails without a server anywhere.</summary>
    private const string Unopenable = "Data Source=./prag507-no-such-directory/x.db";

    private const string Shared = "Data Source=file:prag507-shared?mode=memory&cache=shared";

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);

    /// <summary>
    ///     The assumption the whole feature rests on, pinned because it is EF Core's and not ours.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Nothing in this repository used <c>ConnectionFailed(Async)</c> before this, so whether
    ///     throwing from a notification hook propagates at all had to be established rather than
    ///     assumed. It does, and it <b>replaces</b> the original — which is why the interceptor carries
    ///     the original as the inner exception. An EF upgrade that made this hook swallow exceptions
    ///     would silently take the whole feature away; this case is what would notice.
    /// </remarks>
    [Fact]
    public async Task AnExceptionThrownFromConnectionFailed_ReachesTheCaller()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestDbContext>(o =>
            o.UseSqlite(Unopenable).AddInterceptors(new ThrowingProbe()));

        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(() => ctx.Database.OpenConnectionAsync());

        thrown.Should().BeOfType<InvalidOperationException>(
            "an exception from ConnectionFailedAsync reaches the caller in place of the provider's");
        thrown!.Message.Should().Be(ThrowingProbe.Marker);
    }

    [Fact]
    public async Task ADedicatedTenantsDatabaseThatWillNotOpen_NamesTheTenant()
    {
        await using var sp = BuildProvider(dedicated: Unopenable, shared: Shared);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(() => ctx.Database.OpenConnectionAsync());

        var stated = thrown.Should().BeOfType<TenantDatabaseUnavailableException>(
            "the interceptor is the last place that still knows this connection belonged to a tenant").Subject;

        stated.TenantId.Should().Be("acme");
        stated.Message.Should().Contain("acme", "the tenant is the fact the driver cannot know");
        stated.Message.Should().Contain("ProvisionAsync",
            "a diagnosis becomes an instruction only if it says what to do: provisioning is explicit");
        stated.InnerException.Should().NotBeNull(
            "the provider's own error carries the SQL state and must not be thrown away");
    }

    /// <summary>
    ///     The control: a tenant on the <b>shared</b> database. Nothing was rewritten for it, so its
    ///     failure is not a tenant-database failure and keeps saying what it always said.
    /// </summary>
    /// <remarks>
    ///     Without this, "name the tenant" is satisfied by naming it on every connection failure in the
    ///     application — including the shared database's, which belongs to no tenant.
    /// </remarks>
    [Fact]
    public async Task ATenantOnTheSharedDatabase_KeepsTheProvidersOwnError()
    {
        await using var sp = BuildProvider(dedicated: null, shared: Unopenable);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(() => ctx.Database.OpenConnectionAsync());

        thrown.Should().NotBeOfType<TenantDatabaseUnavailableException>(
            "this tenant has no database of its own, so nothing here is about a tenant's database");
        thrown.Should().NotBeNull("the connection still fails — it is the wording that must not change");
    }

    /// <summary>The same, with no tenant resolved at all.</summary>
    [Fact]
    public async Task NoResolvedTenant_KeepsTheProvidersOwnError()
    {
        await using var sp = BuildProvider(dedicated: null, shared: Unopenable, tenantId: null);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(() => ctx.Database.OpenConnectionAsync());

        thrown.Should().NotBeOfType<TenantDatabaseUnavailableException>();
        thrown.Should().NotBeNull();
    }

    /// <summary>
    ///     ⚠️ Asking is not using: <c>CanConnectAsync</c> still <b>answers</b> rather than throwing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This one had to be measured and could not be guarded. EF Core opens a probe connection
    ///         with errors expected, but that only changes the log level: the failure hook is invoked
    ///         either way, and <c>ConnectionErrorEventData</c> carries <b>no</b> flag that tells a probe
    ///         apart from a real failure — its only property is <c>Exception</c> (EF Core 10.0.10
    ///         reference documentation). So the interceptor cannot decline to speak for a probe.
    ///     </para>
    ///     <para>
    ///         Measured instead: <c>CanConnectAsync</c> swallows what the hook throws and answers false,
    ///         so turning a question into a crash was never on the table. Pinned here because it is EF
    ///         Core's behaviour, not ours, and because a health check or a provisioner asking "is it
    ///         there?" is exactly the caller that must not be made to catch.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AskingWhetherTheDatabaseIsThere_StillAnswersInsteadOfThrowing()
    {
        await using var sp = BuildProvider(dedicated: Unopenable, shared: Shared);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(
            async () => await ctx.Database.CanConnectAsync().ConfigureAwait(false));

        thrown.Should().BeNull(
            "CanConnectAsync asks by failing; a caller that wanted an answer must not get an exception "
            + "because the interceptor had something to say about the failure");
    }

    /// <summary>
    ///     And the control that separates "fails with our sentence" from "fails": a dedicated database
    ///     that <b>does</b> open is not touched.
    /// </summary>
    [Fact]
    public async Task ADedicatedDatabaseThatOpens_IsNotAffected()
    {
        await using var sp = BuildProvider(
            dedicated: "Data Source=file:prag507-acme?mode=memory&cache=shared", shared: Shared);
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var thrown = await Record.ExceptionAsync(() => ctx.Database.OpenConnectionAsync());

        thrown.Should().BeNull("a connection that opens raises no failure hook at all");
        await ctx.Database.CloseConnectionAsync();
    }

    /// <summary>
    ///     The database is named whichever way the provider spells it in a connection string.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The cases above run on SQLite, so without this the sentence would only ever have been read
    ///     with a <c>Data Source</c>. The failure this was reported from is PostgreSQL's, and SQL
    ///     Server spells it a third way — the part of "the message works for other providers" that can
    ///     be settled without a container is settled here.
    /// </remarks>
    [Theory]
    [InlineData("Host=db;Port=5432;Database=casework_tenant_acme;Username=u", "casework_tenant_acme")]
    [InlineData("Server=db;Initial Catalog=CaseworkAcme;Integrated Security=true", "CaseworkAcme")]
    [InlineData("Data Source=./acme.db", "./acme.db")]
    [InlineData("", "(none)")]
    public void TheDatabaseIsNamed_HoweverTheProviderSpellsIt(string connectionString, string expected)
        => Pragmatic.Migrations.Tenant.ConnectionStringInfo.DatabaseIn(connectionString).Should().Be(expected);

    private static ServiceProvider BuildProvider(string? dedicated, string shared, string? tenantId = "acme")
    {
        var store = new StubTenantStore();
        if (tenantId is not null)
            store.Add(new TenantInfo
            {
                TenantId = tenantId,
                TenantName = tenantId,
                State = TenantState.Active,
                CreatedAt = DateTimeOffset.UnixEpoch,
                ConnectionString = dedicated
            });

        var services = new ServiceCollection();
        services.AddSingleton<ITenantStore>(store);
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddDbPerTenant(o => o.DefaultConnectionString = shared);
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite(shared).AddInterceptors(sp.GetServices<IInterceptor>()));

        return services.BuildServiceProvider();
    }

    /// <summary>Throws from the failure hook, to establish what EF Core does with it.</summary>
    private sealed class ThrowingProbe : DbConnectionInterceptor
    {
        public const string Marker = "PRAG507-FROM-CONNECTION-FAILED";

        public override Task ConnectionFailedAsync(
            DbConnection connection, ConnectionErrorEventData eventData, CancellationToken ct = default)
            => throw new InvalidOperationException(Marker);

        public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
            => throw new InvalidOperationException(Marker);
    }

    private sealed class StubTenantStore : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _tenants = new();

        public void Add(TenantInfo tenant) => _tenants[tenant.TenantId] = tenant;

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.TryGetValue(tenantId, out var v) ? v : null);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>([.. _tenants.Values]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) => GetAllAsync(ct);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            Add(tenant);
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Remove(tenantId));

        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.Remove(tenantId));
    }
}
