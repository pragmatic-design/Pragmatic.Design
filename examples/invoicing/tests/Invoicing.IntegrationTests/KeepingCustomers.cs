using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoicing.IntegrationTests.Infrastructure;
using Invoicing.Registry.Contracts;
using Invoicing.Registry.Customers.Queries;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The register of who is billed: codes, rules, and the contract the other module reads a
///     customer through.
/// </summary>
public sealed class KeepingCustomers(PostgresFixture database) : InvoicingTestBase(database)
{
    [Fact]
    public async Task ACreatedCustomer_GetsACodeAndIsReadBack()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", Customer(
            name: "Bianchi S.r.l.", vatNumber: "IT01234567890", culture: "it-IT", paymentTermsDays: 60)));

        var read = await ReadJsonAsync(await accountant.GetAsync($"api/customers/{created.GetProperty("id").GetGuid()}"));

        read.GetProperty("name").GetString().Should().Be("Bianchi S.r.l.");
        read.GetProperty("vatNumber").GetString().Should().Be("IT01234567890");
        read.GetProperty("preferredCulture").GetString().Should().Be("it-IT");
        read.GetProperty("paymentTermsDays").GetInt32().Should().Be(60);
        read.GetProperty("code").GetString().Should().MatchRegex(@"^CUS-\d{5}$");
    }

    /// <summary>
    ///     The code comes from a database sequence, so two customers created one after the other take two
    ///     consecutive numbers — which is what a sequence buys, and all of it.
    /// </summary>
    /// <remarks>
    ///     Not asserted as <c>CUS-00001</c>: the suite shares one database, so the first number of a class
    ///     is whatever the other classes left. The entity's comment says the same thing about tenants —
    ///     the numbers of one company have holes where another's customers fell, which is why an invoice
    ///     number cannot be built this way.
    /// </remarks>
    [Fact]
    public async Task TwoCustomersInARow_TakeConsecutiveCodes()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var first = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", Customer(name: "First")));
        var second = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", Customer(name: "Second")));

        Number(second).Should().Be(Number(first) + 1);

        static int Number(JsonElement customer) =>
            int.Parse(customer.GetProperty("code").GetString()!["CUS-".Length..]);
    }

    /// <summary>
    ///     The two-tenant assertion this story exists for: a VAT number repeats across companies and not
    ///     inside one. It is also the test that fails if the unique index was declared <c>Global</c>.
    /// </summary>
    [Fact]
    public async Task TheSameVatNumber_IsRefusedInTheSameTenant_AndAcceptedInAnother()
    {
        var vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}";
        var acme = As(TestUsers.Accountant, await OnboardAsync("acme"));
        var globex = As(TestUsers.Accountant, await OnboardAsync("globex"));

        await ReadSuccessAsync(await acme.PostAsJsonAsync("api/customers", Customer(vatNumber: vatNumber)));
        var again = await acme.PostAsJsonAsync("api/customers", Customer(vatNumber: vatNumber));
        var elsewhere = await globex.PostAsJsonAsync("api/customers", Customer(vatNumber: vatNumber));

        again.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a repeat inside the company is a mistake, and the answer names it rather than being a 500");
        elsewhere.StatusCode.Should().Be(HttpStatusCode.Created,
            "two companies may legitimately bill the same firm");
    }

    [Fact]
    public async Task AnUnsupportedLanguage_IsRefusedWith422_NamingTheField()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var response = await accountant.PostAsJsonAsync("api/customers", Customer(culture: "fr-FR"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("referredCulture",
            "a refusal a form can act on names the field it is about");
    }

    [Fact]
    public async Task PaymentTermsBeyondTheRange_AreRefused()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());

        var response = await accountant.PostAsJsonAsync("api/customers", Customer(paymentTermsDays: 365));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    ///     The isolation asserted over HTTP, through an operation that writes a tenant row: two companies, a customer each, and each token sees exactly one.
    /// </summary>
    [Fact]
    public async Task EachCompany_SeesOnlyItsOwnCustomers()
    {
        var acme = As(TestUsers.Accountant, await OnboardAsync("acme"));
        var globex = As(TestUsers.Accountant, await OnboardAsync("globex"));

        await ReadSuccessAsync(await acme.PostAsJsonAsync("api/customers", Customer(name: "Only Acme's")));
        await ReadSuccessAsync(await globex.PostAsJsonAsync("api/customers", Customer(name: "Only Globex's")));

        var acmeSees = await NamesAsync(acme);
        var globexSees = await NamesAsync(globex);

        acmeSees.Should().Contain("Only Acme's").And.NotContain("Only Globex's");
        globexSees.Should().Contain("Only Globex's").And.NotContain("Only Acme's");
    }

    /// <summary>
    ///     The control that catches the framework's asymmetry: the interceptor leaves <c>TenantId</c>
    ///     untouched when nothing is resolved, while the read filter is fail-closed — so a wrongly wired
    ///     application writes rows it can never read back, with a 201 and no warning. Counted in the table
    ///     itself, past every filter.
    /// </summary>
    [Fact]
    public async Task NoCustomerRow_IsWrittenWithoutATenant()
    {
        var accountant = As(TestUsers.Accountant, await OnboardAsync());
        await ReadSuccessAsync(await accountant.PostAsJsonAsync("api/customers", Customer()));

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            @"select count(*) from ""Customers"" where ""TenantId"" is null or ""TenantId"" = ''", connection);

        (await command.ExecuteScalarAsync()).Should().Be(0L);
    }

    /// <summary>
    ///     The contract Billing will read a customer through, resolved from the running host's container
    ///     and called. Not ceremony: a published contract whose registration is missing fails at the first
    ///     request that injects it.
    /// </summary>
    [Fact]
    public async Task TheBillingDetailsContract_IsResolvable_AndAnswers()
    {
        var tenant = await OnboardAsync();
        var accountant = As(TestUsers.Accountant, tenant);
        var created = await ReadJsonAsync(await accountant.PostAsJsonAsync("api/customers", Customer(
            name: "Contract S.p.A.", vatNumber: "IT99988877766", paymentTermsDays: 45)));

        using var tenantScope = TenantScope.BeginScope(tenant);
        await using var scope = Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IRegistryReads>();

        var details = await reads.GetCustomerBillingDetails(new GetCustomerBillingDetailsQuery
        {
            CustomerId = created.GetProperty("id").GetGuid(),
        });

        details.Should().HaveCount(1);
        details[0].Name.Should().Be("Contract S.p.A.");
        details[0].PaymentTermsDays.Should().Be(45);
        details[0].Address.City.Should().Be("Milano", "the address travels as one value, not four loose fields");
    }

    private static async Task<List<string?>> NamesAsync(HttpClient client)
    {
        var page = await ReadJsonAsync(await client.GetAsync("api/customers"));

        return [.. page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("name").GetString())];
    }

    private static object Customer(
        string name = "A customer",
        string? vatNumber = null,
        string culture = "en-US",
        int paymentTermsDays = 30) => new
        {
            name,
            vatNumber = vatNumber ?? $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
            email = "billing@customer.test",
            preferredCulture = culture,
            paymentTermsDays,
            addressStreet = "Via Roma 1",
            addressPostCode = "20121",
            addressCity = "Milano",
            addressCountry = "IT",
        };
}
