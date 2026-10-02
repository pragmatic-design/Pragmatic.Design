using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
// The generated registration lives in {AssemblyName}.Generated, which is not an enclosing namespace
// here — an extension method is only found through a using or an enclosing scope.
using Pragmatic.Privacy.Tests.Generated;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     The generated privacy adapters, resolved out of a real container and run against a real
///     database.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot of the generated text says the text has not changed; it cannot say that anything
///         resolves, that the query filters by the right subject, or that the runtime services see
///         anything at all. The three ports — <c>IPersonalDataSource</c>, <c>IErasureStep</c>,
///         <c>IProcessingActivitySource</c> — can lack a production implementation for exactly as long
///         as nobody asks a container for one.
///     </para>
///     <para>
///         Two subjects throughout. One subject cannot distinguish "filters correctly" from "returns
///         the whole table", and the second is the difference between an access request and a data
///         breach.
///     </para>
/// </remarks>
public sealed class GeneratedPrivacyWiringTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly ServiceProvider _services;
    private readonly InMemorySubjectRegistry _registry = new();

    private string _alice = string.Empty;
    private string _bob = string.Empty;

    public GeneratedPrivacyWiringTests()
    {
        _connection.Open();

        var services = new ServiceCollection();

        services.AddDbContext<WiringDbContext>(o => o.UseSqlite(_connection));

        // The adapters ask for a DbContext, unkeyed: these entities declare no [BelongsTo<TBoundary>],
        // which is the single-context shape.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<WiringDbContext>());

        services.AddSingleton<ISubjectRegistry>(_registry);
        services.AddSingleton<IConsentStore, EmptyConsentStore>();

        // The one call the generator produces and the host emits. Everything under test hangs off it —
        // including AddPrivacy(), which it makes itself. Deliberately not called here: an application
        // that had to remember it would get adapters registered into a container where nothing resolves
        // them, and an empty enumerable makes every one of these services report success on no data.
        services.AddGeneratedPrivacyAdapters();

        _services = services.BuildServiceProvider(validateScopes: true);
    }

    public async Task InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WiringDbContext>();
        await db.Database.EnsureCreatedAsync();

        var alice = new Customer { Email = "alice@example.com", FullName = "Alice Rossi" };
        var bob = new Customer { Email = "bob@example.com", FullName = "Bob Bianchi" };

        db.Customers.AddRange(alice, bob);
        await db.SaveChangesAsync();

        db.Orders.AddRange(
            new Order { CustomerId = alice.Id, ShippingAddress = "Via Alice 1" },
            new Order { CustomerId = alice.Id, ShippingAddress = "Via Alice 2" },
            new Order { CustomerId = bob.Id, ShippingAddress = "Via Bob 9" });
        await db.SaveChangesAsync();

        _alice = await _registry.GetOrCreateReferenceAsync(nameof(Customer), alice.Email);
        _bob = await _registry.GetOrCreateReferenceAsync(nameof(Customer), bob.Email);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void BothClassifiedEntities_ResolveAsPersonalDataSources()
    {
        using var scope = _services.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IPersonalDataSource>().ToList();

        sources.Select(s => s.Category).Should().BeEquivalentTo([
            typeof(Customer).FullName!,
            typeof(Order).FullName!
        ]);
    }

    [Fact]
    public async Task AccessRequest_ReturnsTheSubjectsRowsFromEveryEntityThatHoldsThem()
    {
        using var scope = _services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<SubjectAccessService>();

        var export = await access.CollectAsync(_alice);

        export.Categories.Should().ContainKey(typeof(Customer).FullName!);
        export.Categories.Should().ContainKey(typeof(Order).FullName!);
        export.Categories[typeof(Customer).FullName!].Should().HaveCount(1);
        export.Categories[typeof(Order).FullName!].Should().HaveCount(2);
    }

    [Fact]
    public async Task AccessRequest_ReturnsNothingBelongingToAnotherSubject()
    {
        using var scope = _services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<SubjectAccessService>();

        var export = await access.CollectAsync(_alice);

        var addresses = export.Categories[typeof(Order).FullName!]
            .Select(row => (string?)row["ShippingAddress"])
            .ToList();

        addresses.Should().BeEquivalentTo(["Via Alice 1", "Via Alice 2"]);
        addresses.Should().NotContain("Via Bob 9");
    }

    [Fact]
    public async Task AccessRequest_CarriesTheClassifiedFieldsTheExtractorProjects()
    {
        using var scope = _services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<SubjectAccessService>();

        var export = await access.CollectAsync(_alice);
        var customer = export.Categories[typeof(Customer).FullName!].Single();

        customer["Email"].Should().Be("alice@example.com");
        customer["FullName"].Should().Be("Alice Rossi");
        // Not classified, so not part of the answer.
        customer.Should().NotContainKey("Id");
    }

    [Fact]
    public async Task UnknownSubjectReference_CollectsNothing()
    {
        using var scope = _services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<SubjectAccessService>();

        var export = await access.CollectAsync("ref-does-not-exist");

        export.RecordCount.Should().Be(0);
    }

    [Fact]
    public void BothClassifiedEntities_ResolveAsErasureSteps()
    {
        using var scope = _services.CreateScope();

        var steps = scope.ServiceProvider.GetServices<IErasureStep>().OrderBy(s => s.Order).ToList();

        steps.Select(s => s.Name).Should().BeEquivalentTo([
            typeof(Order).FullName!,
            typeof(Customer).FullName!
        ]);

        steps[0].Name.Should().Be(
            typeof(Order).FullName!,
            "the row holding the foreign key has to be cleared before the one it points at");
    }

    [Fact]
    public async Task Erasure_ClearsTheSubjectsFieldsAndLeavesTheOtherSubjectUntouched()
    {
        using (var scope = _services.CreateScope())
        {
            var orchestrator = scope.ServiceProvider.GetRequiredService<ErasureOrchestrator>();
            var outcome = await orchestrator.EraseAsync(_alice);

            outcome.ErasedCount.Should().Be(3, "one customer row and two orders");
            outcome.Retained.Select(r => r.What).Should().Contain("Customer.Email");
        }

        await using var verify = _services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<WiringDbContext>();

        var alice = await db.Customers.SingleAsync(c => c.Email == "alice@example.com");
        alice.FullName.Should().BeNull("the erasure plan nulls it");

        var aliceOrders = await db.Orders.Where(o => o.CustomerId == alice.Id).ToListAsync();
        aliceOrders.Select(o => o.ShippingAddress).Should().AllSatisfy(a => a.Should().BeNull());

        var bob = await db.Customers.SingleAsync(c => c.Email == "bob@example.com");
        bob.FullName.Should().Be("Bob Bianchi", "erasing one subject must not touch another");
        (await db.Orders.SingleAsync(o => o.CustomerId == bob.Id)).ShippingAddress.Should().Be("Via Bob 9");
    }

    [Fact]
    public async Task ProcessingRegister_DescribesEveryClassifiedEntity()
    {
        using var scope = _services.CreateScope();
        var builder = scope.ServiceProvider.GetRequiredService<ProcessingRegisterBuilder>();

        var register = await builder.BuildAsync();

        register.Activities.Select(a => a.EntityType).Should().BeEquivalentTo([
            typeof(Customer).FullName!,
            typeof(Order).FullName!
        ]);

        var customer = register.Activities.Single(a => a.EntityType == typeof(Customer).FullName);
        customer.IsDataSubject.Should().BeTrue();
        customer.Categories.Should().BeEquivalentTo(["Contact", "Identity"]);
        customer.Erasure["FullName"].Should().Be("Null");
        customer.Retained.Single().What.Should().Be("Customer.Email");
    }

    /// <summary>
    ///     <c>Customer.Email</c> is retained in the clear: the generated plan says keeping it does not need
    ///     the subject's key, so it cannot stop the key's destruction from erasing what relies on it.
    /// </summary>
    [Fact]
    public void AFieldRetainedInTheClear_IsDeclaredNotToNeedTheKey()
    {
        var email = CustomerErasurePlan.Retained.Single();

        email.Field.Should().Be("Email");
        email.RequiresKey.Should().BeFalse();
    }
}
