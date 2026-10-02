using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     Boots <b>both</b> services for the generated contract tests, and gives the generated identity a
///     real one.
/// </summary>
/// <remarks>
///     <para>
///         <b>Two hosts, a client each.</b> The generated tests are emitted per boundary —
///         <c>IntakeCrud…</c>, <c>VerifyAuth…</c> — and each class says which boundary it is, so this fixture answers the only question an application can answer: which host that
///         boundary runs in. ⚠️ Not one <c>HttpMessageHandler</c> behind the one
///         <c>PragmaticContractHost.Client</c> routing by <b>route prefix</b>, which is fragile in a
///         way that reads as somebody else's failure: a route added to Verify under a prefix the table
///         does not name goes to Intake and answers 404.
///     </para>
///     <para>
///         ⚠️ Both tenants the isolation test uses have to exist as organisations in <b>both</b>
///         registers: with <c>RequireKnownTenant</c> an unknown tenant is answered 404 everywhere, and a
///         404 on the read satisfies "not visible to another tenant" for the wrong reason — the strongest
///         way there is to make an isolation test pass without isolating anything.
///     </para>
///     <para>
///         The privileged caller holds <b>both</b> of Intake's roles and no role at all is given to the
///         one the generated tests expect to be refused. The generator emits the operation's
///         <b>permission</b>, and a host that maps roles to permissions (<c>UseAuthorization</c>) stops
///         honouring a raw permission claim — so a permission on a header would be ignored and every
///         privileged contract would be a 403. Which roles cover which permissions is an application's
///         own table, and this is where it is handed over.
///     </para>
/// </remarks>
public sealed class ContractFixture : IAsyncLifetime
{
    /// <summary>The two organisations the generated isolation test writes into its requests.</summary>
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

    private static readonly JsonSerializerOptions Json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    ///     The two boundaries, as the generator names them — the <c>[Boundary]</c> class without its
    ///     suffix, which is what the generated classes are called after.
    /// </summary>
    /// <remarks>
    ///     Written out because only the application knows which process a boundary runs in: the generator
    ///     knows there are two, and nothing in the framework can know that this one is two deployments.
    /// </remarks>
    private const string IntakeBoundary = "Intake";

    private const string VerifyBoundary = "Verify";

    private readonly PostgresFixture _databases = new();
    private readonly RabbitMqFixture _broker = new();
    private IntakeWebFactory _intake = null!;
    private VerifyWebFactory _verify = null!;
    private HttpClient _intakeClient = null!;
    private HttpClient _verifyClient = null!;

    public async Task InitializeAsync()
    {
        await _databases.InitializeAsync();
        await _broker.InitializeAsync();

        _intake = new IntakeWebFactory(
            _databases.IntakeConnectionString, _broker.ConnectionString,
            _databases.ConnectionStringTemplate);
        _verify = new VerifyWebFactory(
            _databases.VerifyConnectionString, _broker.ConnectionString,
            _databases.VerifyConnectionStringTemplate);

        _intakeClient = _intake.CreateClient();
        _verifyClient = _verify.CreateClient();

        // One client per boundary, and no PragmaticContractHost.Client at all: with a per-boundary
        // client registered, a boundary nobody registered is an error instead of a quiet fall-through to
        // the other service's host.
        PragmaticContractHost.UseClientFor(IntakeBoundary, _intakeClient);
        PragmaticContractHost.UseClientFor(VerifyBoundary, _verifyClient);

        // Both organisations in both registers, or every request is a 404. Through each service's own
        // ITenantStore, which is what a deployment writes and what onboarding writes.
        foreach (var tenant in new[] { TenantA, TenantB })
        {
            await Onboard(_intake.Services, tenant);
            await Onboard(_verify.Services, tenant);
        }

        // What the generator cannot fill from a shape. Two, and no more: a hook per operation would be
        // the application writing the tests it is being checked by.
        PragmaticContractHost.BodyFor = operation => operation switch
        {
            // A case needs a subject of at least ten characters (the aggregate's own invariant) and a
            // language this host speaks — the generator fills both with twelve random hex characters,
            // which is long enough and is not a culture.
            "OpenCaseMutation" => new
            {
                subject = "A licence for a food stall, contract",
                applicant = "C. Applicant",
                applicantEmail = "contract@example.org",
                applicantLanguage = "en-US",
            },
            // An organisation's id ends up in a database name, so it may hold letters, digits, '-' and
            // '_' only; and `dedicatedDatabase` stays false, because a contract test is not the place to
            // provision a database.
            "OnboardAnOrganisationAction" => new
            {
                tenantKey = $"contract{Random.Shared.Next(100_000, 999_999)}",
                name = "Contract Organisation",
                dedicatedDatabase = false,
            },
            _ => null,
        };

        // A verification is never created over HTTP: Intake asks for one, the request crosses the broker, and
        // Verify writes it. The transition contracts of Verification need one in its initial state, and only
        // this application knows the way there.
        PragmaticContractHost.ArrangeFor = async (entity, _) => entity switch
        {
            "Verification" => (await AVerificationAsync()).ToString(),
            _ => null,
        };

        PragmaticContractHost.PrepareRequest = contract =>
        {
            var request = contract.Message;

            // Read before removing: the isolation test says tenant-a on the create and tenant-b on the
            // read, and that difference is the only thing it measures.
            var tenant = First(request, PragmaticTestIdentity.TenantIdHeader) ?? TenantA;
            // ⚠️ By the **user id** the generated test wrote, not by the operation's name: `operation` is
            // the endpoint's name (`DownloadCaseDocument`), the same for both halves of the pair, so a
            // check on it gave the privileged token to the caller that has to be refused — and both
            // halves then answered 404, which the framework's own assertion caught as "the permission
            // changed nothing observable".
            var refused = First(request, PragmaticTestIdentity.UserIdHeader) == Underprivileged;

            foreach (var header in HeaderIdentity)
                request.Headers.Remove(header);

            // ⚠️ **Both** of Intake's roles for the privileged caller, and that is the story's own
            // lesson: the generator emits the operation's permission, and this host maps roles to
            // permissions — so a caller holding only `caseworker` is refused `api/organisations`, which
            // belongs to `service-operator`. A contract test cannot know an application's role table;
            // the application has to hand it a caller who holds everything the contracts ask for.
            // ⚠️ Whose token, decided by the **boundary** the generated class declares and not by the
            // request's path: the two services sign with different keys, and a path is a table somebody
            // has to keep in step with the routes.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                contract.Boundary == VerifyBoundary
                    ? TestTokens.For(
                        _verify.Services, "contract", tenant,
                        refused ? [] : ["verification-service"])
                    : TestTokens.For(
                        _intake.Services, "contract", tenant,
                        refused ? [] : ["caseworker", "service-operator"]));
        };
    }

    /// <summary>
    ///     A verification in its initial state, the way production makes one: a case opened and asked to be
    ///     verified in Intake, the request carried to Verify by the broker.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Verify binds its queue in the background after it connects, and a message published before
    ///     that is discarded by the exchange — so the queue is waited for before the question is asked.
    /// </remarks>
    private async Task<Guid> AVerificationAsync()
    {
        await EventuallyAsync(
            () => _broker.QueueExistsAsync("verification-requested"),
            "Verify's consumer never bound its queue");

        var caseworker = new AuthenticationHeaderValue(
            "Bearer", TestTokens.For(_intake.Services, "contract", TenantA, ["caseworker", "service-operator"]));

        using var open = new HttpRequestMessage(HttpMethod.Post, "api/cases")
        {
            Content = JsonContent.Create(new
            {
                subject = "A licence for a food stall, contract",
                applicant = "C. Applicant",
                applicantEmail = "contract@example.org",
                applicantLanguage = "en-US",
            }, options: Json),
        };
        open.Headers.Authorization = caseworker;
        var caseId = await (await _intakeClient.SendAsync(open)).ShouldIdentifyTheCreatedAsync();

        using var ask = new HttpRequestMessage(HttpMethod.Post, $"api/cases/{caseId}/verifications")
        {
            Content = JsonContent.Create(new { kind = "identity" }, options: Json),
        };
        ask.Headers.Authorization = caseworker;
        PragmaticHttpAssertions.ShouldBeSuccess(await _intakeClient.SendAsync(ask));

        Guid? verification = null;
        await EventuallyAsync(
            async () => (verification = await VerificationForAsync(caseId)) is not null,
            $"Verify wrote a verification for case {caseId}");

        return verification!.Value;
    }

    private async Task<Guid?> VerificationForAsync(string caseId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(_databases.VerifyConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "select \"PersistenceId\" from \"Verifications\" where \"CaseId\" = @caseId", connection);
        command.Parameters.AddWithValue("caseId", Guid.Parse(caseId));
        return await command.ExecuteScalarAsync() as Guid?;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string what, int timeoutSeconds = 20)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            if (await condition())
                return;

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"waited {timeoutSeconds}s and it never happened: {what}");
    }

    private static async Task Onboard(IServiceProvider services, string tenantId)
    {
        var store = services.GetRequiredService<ITenantStore>();

        if (await store.GetByIdAsync(tenantId) is not null)
            return;

        await store.CreateAsync(new TenantInfo
        {
            TenantId = tenantId,
            TenantName = tenantId,
            ConnectionString = null,
            State = TenantState.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string? First(HttpRequestMessage request, string header)
        => request.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    public async Task DisposeAsync()
    {
        PragmaticContractHost.Reset();
        _intakeClient.Dispose();
        _verifyClient.Dispose();
        await _intake.DisposeAsync();
        await _verify.DisposeAsync();
        await _broker.DisposeAsync();
        await _databases.DisposeAsync();
    }
}

[CollectionDefinition(PragmaticContractHost.Collection)]
public sealed class ContractTestCollection : ICollectionFixture<ContractFixture>;
