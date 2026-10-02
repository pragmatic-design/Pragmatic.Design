using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     Boots the application for the generated contract tests, and gives the generated identity a real one.
/// </summary>
/// <remarks>
///     <para>
///         The generated tests authenticate with <c>PragmaticTestIdentity.AsUser</c>, which writes
///         <c>X-User-*</c> and <c>X-Tenant-Id</c> — headers read by <c>HeaderUserMiddleware</c>, which is
///         <b>development-only and throws outside Development</b>. Invoicing authenticates with OIDC in
///         <c>Testing</c> on purpose, so left alone every generated test would be a 401.
///     </para>
///     <para>
///         <c>PragmaticContractHost.PrepareRequest</c> is the seam the framework provides for exactly this:
///         the application gets the request last. Here it reads the tenant the generated test asked for,
///         drops the header identity, and puts a token from this suite's own provider in its place — which
///         is the point, because the test that matters
///         (<c>…_IsNotVisibleToAnotherTenant</c>) only means something if the two requests really are two
///         tenants.
///     </para>
///     <para>
///         The role is <c>platform-administrator</c> for every privileged request, deliberately: the
///         generated test knows the operation's <em>permission</em>, and a host that maps roles to
///         permissions stops honouring a raw permission claim. Least privilege is asserted by the access table in
///         <c>SigningInWithTheProvider</c>; what is asserted here is isolation.
///     </para>
/// </remarks>
public sealed class ContractAppFixture : IAsyncLifetime
{
    /// <summary>The two companies the generated isolation test writes into its requests.</summary>
    private const string TenantA = "tenant-a";

    private const string TenantB = "tenant-b";

    /// <summary>The user id the generated tests give the caller that must be refused.</summary>
    private const string Underprivileged = "contract-noperm";

    private static readonly string[] HeaderIdentity =
    [
        PragmaticTestIdentity.UserIdHeader,
        PragmaticTestIdentity.UserNameHeader,
        PragmaticTestIdentity.TenantIdHeader,
        PragmaticTestIdentity.PermissionsHeader,
        PragmaticTestIdentity.RolesHeader,
        PragmaticTestIdentity.GroupsHeader,
    ];

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly PostgresFixture _database = new();
    private InvoicingWebFactory? _factory;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = new InvoicingWebFactory(_database.ConnectionString, services: TestIdentityProvider.Configure);

        var client = _factory.CreateClient();
        PragmaticContractHost.Client = client;

        // Both tenants have to exist as companies, or the RequireKnownTenant guard answers 404 to every
        // request of the collection — and a 404 everywhere satisfies the isolation assertion for the wrong
        // reason.
        await OnboardAsync(client, TenantA);
        await OnboardAsync(client, TenantB);

        // The invoice's create needs a customer that already exists, in the company the generated tests
        // write into. Seeded once, here, because the hook is synchronous and this is the only place that
        // can await — and in tenant-a, which is both the tenant the isolation test creates in and the
        // default the request preparation below gives a request that names none.
        var customerId = await ACustomerAsync(client, TenantA);

        // What the generator cannot fill from a shape: a culture that is one of the two this application
        // speaks, and — for the invoice — a customer id and the lines, a nested collection of another
        // mutation. Everything else it synthesises is acceptable: the VAT number and the e-mail are unique
        // per call, which they have to be, because [Unique] is per tenant and two tests create a customer
        // in the same one.
        PragmaticContractHost.BodyFor = operation => operation switch
        {
            "CreateCustomerMutation" => Customer(),
            // One key for one endpoint: the CRUD contract and the state-transition contract — which
            // creates the invoice to have something to move — both ask under the create operation's own
            // name.
            "CreateDraftInvoiceMutation" => Draft(customerId),
            // The transition's own body, under the transition's own name. Only this one: voiding needs a
            // reason, which is a string the generator fills, and issuing takes nothing but its route — a
            // payment needs a Money, and a complex type is not something a shape can invent.
            "Invoice_TransitionToPaid" => Payment(),
            _ => null
        };

        PragmaticContractHost.PrepareRequest = contract =>
        {
            var request = contract.Message;

            // Read before removing: the isolation test says tenant-a on the create and tenant-b on the
            // read, and that difference is the only thing it is measuring.
            var tenant = First(request, PragmaticTestIdentity.TenantIdHeader) ?? TenantA;
            var refused = First(request, PragmaticTestIdentity.UserIdHeader) == Underprivileged;

            foreach (var header in HeaderIdentity)
                request.Headers.Remove(header);

            // A caller with no role holds no permission, which is what the "without the required
            // permission" contracts need — and it is refused by the application's own gate, not by a
            // header the application does not read.
            var user = refused ? TestUsers.WithoutRole : TestUsers.PlatformAdministrator;

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", TestIdentityProvider.Token(user, tenant));
        };
    }

    /// <summary>A customer the generated create of an invoice can name, in <paramref name="tenant" />.</summary>
    /// <remarks>
    ///     Created over the API, as everything else in this fixture is: a row written behind the
    ///     application's back would not prove that the invoice's create can find it.
    /// </remarks>
    private static async Task<Guid> ACustomerAsync(HttpClient client, string tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/customers")
        {
            Content = JsonContent.Create(Customer(), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestIdentityProvider.Token(TestUsers.PlatformAdministrator, tenant));

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"seeding a customer in {tenant}: {(int)response.StatusCode} "
                + await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
    }

    /// <summary>A draft this application accepts: a customer that exists, and one line.</summary>
    private static object Draft(Guid customerId) => new
    {
        customerId,
        lines = new object[]
        {
            new
            {
                description = "Contract line",
                quantity = 1m,
                unitPrice = new { amount = 100m, currency = "EUR" },
                vatRate = 22m,
            },
        },
    };

    /// <summary>A payment body this application accepts, in the currency the draft is written in.</summary>
    /// <remarks>
    ///     It has to pass the action's own validation — <c>[PositiveMoney]</c>, a currency that matches —
    ///     because the contract for this transition asserts a <b>409</b>: a draft is not payable until it
    ///     is issued, and a 422 from a body the fixture got wrong would look like the same refusal while
    ///     measuring nothing.
    /// </remarks>
    private static object Payment() => new
    {
        paidOn = DateOnly.FromDateTime(DateTime.UtcNow),
        amount = new { amount = 100m, currency = "EUR" },
        method = "BankTransfer",
    };

    /// <summary>A customer body this application accepts, unique per call.</summary>
    private static object Customer() => new
    {
        name = $"Contract {Guid.NewGuid():N}"[..20],
        vatNumber = $"IT{Random.Shared.NextInt64(10_000_000_000, 99_999_999_999)}",
        email = $"contract.{Guid.NewGuid():N}@example.com",
        preferredCulture = "it-IT",
        paymentTermsDays = 30,
        addressStreet = "Via Roma 1",
        addressPostCode = "20121",
        addressCity = "Milano",
        addressCountry = "IT",
    };

    /// <summary>A company, onboarded the way the application onboards one: outside any tenant.</summary>
    private static async Task OnboardAsync(HttpClient client, string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/organizations/onboard")
        {
            Content = JsonContent.Create(new
            {
                slug,
                legalName = $"{slug} S.p.A.",
                invoiceNumberPrefix = slug.Replace("-", "", StringComparison.Ordinal).ToUpperInvariant(),
                senderEmail = $"billing@{slug}.test",
            }, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestIdentityProvider.Token(TestUsers.PlatformAdministrator));

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException(
                $"seeding {slug}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static string? First(HttpRequestMessage request, string header)
        => request.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    public async Task DisposeAsync()
    {
        PragmaticContractHost.Reset();
        _factory?.Dispose();
        await _database.DisposeAsync();
    }
}

[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractAppFixture>;
