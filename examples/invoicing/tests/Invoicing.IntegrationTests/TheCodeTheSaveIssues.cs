using System.Net.Http.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Entities;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     A <c>[Raises&lt;T&gt;]</c> event carries the code the <b>save</b> issued, through a
///     running host and into a handler.
/// </summary>
/// <remarks>
///     <para>
///         <c>Customer.Code</c> is <c>[GeneratedValue("CUS-{SEQ:5}")]</c>: the unit of work fills it at
///         the top of the save, so a value exists only after it. A create mutation's declared events used
///         to be constructed before that save, so <c>CustomerRegistered.Code</c> arrived at every handler
///         as the empty string — and silently, because the dispatch is post-commit, so by the time a
///         handler runs the row really does have its code.
///     </para>
///     <para>
///         The hermetic measurement is <c>ADeclaredEventCarriesWhatTheSaveFilledTests</c>, on a
///         hand-written invoker. This is the same claim through the generated one, a real unit of work
///         and a real sequence — which is where it was met: Time off hit it and worked around it by
///         reading the employee back.
///     </para>
///     <para>
///         ⚠️ What is read is the <b>audit entry</b>, not the event: the trail is the handler's only
///         effect, and an event whose payload nothing observes is a declaration. The entry's detail is
///         <c>{code}: {name}</c>, so one row answers both the subject and its control.
///     </para>
/// </remarks>
public sealed class TheCodeTheSaveIssues(PostgresFixture database) : InvoicingTestBase(database)
{
    /// <summary>
    ///     Spelled out rather than read from the module's constant, as the sibling suite does: a test
    ///     that reuses the constant passes when its value changes, which a name on a stored row must not.
    /// </summary>
    private const string CustomerRegistered = "Registry.CustomerRegistered";

    /// <summary>What the trail calls a customer.</summary>
    private const string RegistryTarget = nameof(Customer);

    [Fact]
    public async Task TheCodeReachesTheHandler_AndIsTheOneTheRowGot()
    {
        var tenant = await OnboardAsync("issued");
        var accountant = As(TestUsers.Accountant, tenant);

        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", ACustomer(tenant)));
        var customer = created.GetProperty("id").GetGuid();
        var code = created.GetProperty("code").GetString();

        code.Should().MatchRegex(@"^CUS-\d{5}$", "the sequence issued it during the save");

        var detail = await DetailOfAsync(tenant, customer);

        detail.Should().NotBeNull(
            $"the handler of {CustomerRegistered} is the only thing that writes this entry, so no entry "
            + "means the event never reached it");

        detail!.Should().StartWith($"{code}:",
            "the event carries the code the row was given — it used to carry the empty string, because it "
            + "was constructed before the save that issues it, and nothing said so");
    }

    /// <summary>
    ///     The control: a field fed by one of the mutation's own inputs is carried by the same event.
    /// </summary>
    /// <remarks>
    ///     Without it, "the code is right" would also be satisfied by a handler that read the row back,
    ///     and by a fix that moved the whole construction somewhere it lost the rest of the payload.
    /// </remarks>
    [Fact]
    public async Task AnInputOfTheMutation_ReachesTheSameEvent()
    {
        var tenant = await OnboardAsync("input");
        var accountant = As(TestUsers.Accountant, tenant);

        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", ACustomer(tenant)));

        var detail = await DetailOfAsync(tenant, created.GetProperty("id").GetGuid());

        detail.Should().EndWith("A customer of record",
            "Name comes from the request, not from the save, so where the event is built does not move it");
    }

    private static object ACustomer(string tenant) => new
    {
        name = "A customer of record",
        vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
        email = $"billing@{tenant}.customer.test",
        preferredCulture = "it-IT",
        paymentTermsDays = 30,
        addressStreet = "Via Roma 1",
        addressPostCode = "20121",
        addressCity = "Milano",
        addressCountry = "IT",
    };

    /// <summary>The detail of the one registration entry for a customer, or null when there is none.</summary>
    private async Task<string?> DetailOfAsync(string tenant, Guid customer)
    {
        using var scope = TenantScope.BeginScope(tenant);
        await using var services = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var page = await services.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = RegistryTarget, TargetId = customer.ToString() });

        // Single, not First: two entries for one registration would mean the handler ran twice, and a
        // control that took the first would not notice.
        return page.Entries.SingleOrDefault(e => e.Operation == CustomerRegistered)?.Detail;
    }
}
