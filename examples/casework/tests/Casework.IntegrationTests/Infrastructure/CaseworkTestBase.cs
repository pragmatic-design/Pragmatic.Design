using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.MultiTenancy;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     A test against the running application — which here means <b>both</b> processes: a host per
///     service per test class, each on its own database, both on the one broker.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class CaseworkTestBase : IAsyncLifetime
{
    private readonly List<HttpClient> _clients = [];
    private readonly PostgresFixture _databases;
    private readonly RabbitMqFixture _broker;
    private IntakeWebFactory _intake = null!;
    private VerifyWebFactory _verify = null!;

    protected CaseworkTestBase(PostgresFixture databases, RabbitMqFixture broker)
    {
        _databases = databases;
        _broker = broker;
    }

    /// <summary>A client to Intake, with no token.</summary>
    protected HttpClient Intake { get; private set; } = null!;

    /// <summary>
    ///     A client to Intake carrying a caller: a subject, a tenant, and the roles the token claims.
    /// </summary>
    /// <remarks>
    ///     The tenant is in the <b>token</b> and not in a header, because that is where Intake reads it
    ///     from. A header would be input the caller controls, and an authenticated caller naming another
    ///     organisation's id is the escalation the claim closes.
    /// </remarks>
    protected HttpClient IntakeAs(string role, string tenant = Tenant, string subject = "a.caseworker")
    {
        var client = Track(_intake.CreateClient());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.For(IntakeServices, subject, tenant, role));

        return client;
    }

    /// <summary>A client to Verify.</summary>
    protected HttpClient Verify { get; private set; } = null!;

    /// <summary>
    ///     A client to Verify carrying a <b>service</b>: a subject that is a system, not a person.
    /// </summary>
    /// <remarks>
    ///     Verify holds no accounts, so its one caller that is not the bus is another system — and it
    ///     still arrives with a tenant, because a verification belongs to the organisation whose case it
    ///     is about and Verify's own read filter is fail-closed without one.
    /// </remarks>
    protected HttpClient VerifyAs(string role, string tenant = Tenant, string subject = "intake-operations")
    {
        var client = Track(_verify.CreateClient());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.For(VerifyServices, subject, tenant, role));

        return client;
    }

    protected IServiceProvider IntakeServices => _intake.Services;

    /// <summary>Where Intake's documents land in this run — a directory of the suite's own.</summary>
    protected string IntakeStorageRoot => _intake.StorageRoot;

    protected IServiceProvider VerifyServices => _verify.Services;

    /// <summary>Intake's database, for a test that reads it directly.</summary>
    protected string IntakeConnectionString => _databases.IntakeConnectionString;

    /// <summary>Verify's database. Reading it is how a test proves a message arrived.</summary>
    protected string VerifyConnectionString => _databases.VerifyConnectionString;

    /// <summary>One organisation's own database — the same string the host builds.</summary>
    protected string ConnectionStringFor(string tenantId) => _databases.ConnectionStringFor(tenantId);

    /// <summary>The same in Verify — a different database, as it must be.</summary>
    protected string VerifyConnectionStringFor(string tenantId)
        => _databases.VerifyConnectionStringFor(tenantId);

    /// <summary>How many messages are waiting in the broker queues whose name contains the fragment.</summary>
    protected Task<int> MessagesInAsync(string queueFragment)
        => _broker.MessagesInAsync(queueFragment);

    /// <summary>
    ///     Binds a queue of the test's own to an exchange, so what crossed the broker can be read.
    ///     See <see cref="RabbitMqFixture.ObserveAsync" /> for why a header needs one.
    /// </summary>
    protected Task<string> ObserveAsync(string exchange) => _broker.ObserveAsync(exchange);

    /// <summary>What is sitting in an observer queue, left where it is.</summary>
    protected Task<IReadOnlyList<(string Body, IReadOnlyDictionary<string, string> Headers)>>
        MessagesOnAsync(string queue) => _broker.MessagesOnAsync(queue);

    /// <summary>Removes an observer queue when the test is done with it.</summary>
    protected Task StopObservingAsync(string queue) => _broker.StopObservingAsync(queue);

    /// <summary>
    ///     Waits until the consuming service has bound its queue to the broker.
    /// </summary>
    /// <remarks>
    ///     Publishing before that loses the message: the exchange is a topic exchange, and a message with
    ///     no queue bound to it is discarded. See <see cref="RabbitMqFixture.QueueExistsAsync" />.
    /// </remarks>
    protected async Task WaitForSubscriberAsync(string queueFragment)
        => await EventuallyAsync(
            () => _broker.QueueExistsAsync(queueFragment),
            $"a queue whose name contains '{queueFragment}' was declared — the consumer never bound");

    public Task InitializeAsync()
    {
        _intake = new IntakeWebFactory(
            IntakeConnectionString, _broker.ConnectionString, _databases.ConnectionStringTemplate,
            IntakeSettings, ConfigureIntake);
        _verify = new VerifyWebFactory(
            VerifyConnectionString, _broker.ConnectionString, _databases.VerifyConnectionStringTemplate,
            VerifySettings, ConfigureVerify);

        Intake = Track(_intake.CreateClient());
        Verify = Track(_verify.CreateClient());

        return OnboardTheSuitesTenantAsync();
    }

    /// <summary>
    ///     Registers <see cref="Tenant" /> as an organisation on the shared schema.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it every write is a <b>404</b>: with <c>RequireKnownTenant = true</c> a tenant that
    ///     names no organisation is refused, which is the point of the setting — so the suite's own
    ///     tenant has to exist like any other. Without a connection string, because most tests are about
    ///     everything except which database a row is in, and the shared schema is where they write.
    /// </remarks>
    private async Task OnboardTheSuitesTenantAsync()
    {
        // In **both** services: each has a register of its own, and Verify refuses an
        // unknown tenant on its own API just as Intake does.
        await OnboardAsync(_intake.Services, Tenant, null);
        await OnboardAsync(_verify.Services, Tenant, null);
    }

    /// <summary>
    ///     Registers an organisation in one service's register, with a database of its own or without.
    /// </summary>
    /// <remarks>
    ///     Through that service's own <c>ITenantStore</c>, which is what a deployment writes and what
    ///     onboarding writes. ⚠️ It does <b>not</b> provision the database the connection
    ///     string names — nothing in the framework provisions on first access — so a test that
    ///     wants a dedicated database creates and schema's it itself.
    /// </remarks>
    protected static async Task OnboardAsync(
        IServiceProvider services, string tenantId, string? connectionString,
        TenantState state = TenantState.Active, string? tenantName = null)
    {
        var store = services.GetRequiredService<ITenantStore>();

        if (await store.GetByIdAsync(tenantId) is not null)
            return;

        await store.CreateAsync(new TenantInfo
        {
            TenantId = tenantId,
            // ⚠️ The id by default, and a name of its own where a test asserts on the name: the two are
            // the same string in most of this suite, so an assertion that means "the organisation's
            // name" is indistinguishable there from one that would pass on the id.
            TenantName = tenantName ?? tenantId,
            ConnectionString = connectionString,
            State = state,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Every database on the test server, by name — what a provisioner leaves behind.</summary>
    /// <remarks>
    ///     <c>OnboardingCrossesBothServices</c> asserts a provisioned database by listing them: "the call did not throw" is satisfied
    ///     by a provisioner that does nothing, and <c>NoOpTenantProvisioner</c> is the framework default.
    /// </remarks>
    protected async Task<IReadOnlyList<string>> DatabasesAsync()
    {
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(IntakeConnectionString) { Database = "postgres" }
                .ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("select datname from pg_database", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var databases = new List<string>();
        while (await reader.ReadAsync())
            databases.Add(reader.GetString(0));

        return databases;
    }

    /// <summary>The database a connection string names.</summary>
    protected static string DatabaseIn(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString).Database!;

    /// <summary>
    ///     What one service register says about an organisation, as the column holds it.
    /// </summary>
    /// <remarks>
    ///     Read from the database rather than through <c>ITenantStore</c> so the assertion is about what
    ///     the other process wrote, not about what this one would answer. ⚠️ An <c>int</c>, because that
    ///     is how the enum is stored — the caller compares it against its own service's
    ///     <c>OrganisationState</c>, and the two services have one each.
    /// </remarks>
    protected static async Task<int?> StateOfAsync(string connectionString, string tenantId)
        => await ScalarAsync(
            connectionString,
            $"select \"State\" from \"Organisations\" where \"TenantKey\" = '{tenantId}'") as int?;

    private HttpClient Track(HttpClient client)
    {
        _clients.Add(client);
        return client;
    }

    /// <summary>Configuration Intake needs on top of the suite's, applied after it.</summary>
    protected virtual IReadOnlyDictionary<string, string?> IntakeSettings => new Dictionary<string, string?>();

    /// <summary>Configuration Verify needs on top of the suite's.</summary>
    protected virtual IReadOnlyDictionary<string, string?> VerifySettings => new Dictionary<string, string?>();

    /// <summary>Services Intake replaces on top of the suite's, registered after them.</summary>
    protected virtual void ConfigureIntake(IServiceCollection services)
    {
    }

    /// <summary>Services Verify replaces.</summary>
    protected virtual void ConfigureVerify(IServiceCollection services)
    {
    }

    /// <summary>
    ///     Waits for something the other process will do, or fails saying what never happened.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Polling, and deliberately: delivery over a broker is asynchronous by definition, so there is
    ///     no handle to await — the alternative would be an in-process harness, which is exactly the thing
    ///     these tests exist not to be. The timeout is named and generous; the failure message says what
    ///     was expected, because "the assertion timed out" is not a diagnosis.
    /// </remarks>
    protected static async Task EventuallyAsync(
        Func<Task<bool>> condition, string what, int timeoutSeconds = 20)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            if (await condition())
                return;

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException(
            $"waited {timeoutSeconds}s and it never happened: {what}");
    }

    /// <summary>
    ///     The tenant a test's direct call to an operation runs in.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An operation always runs inside a tenant, and a test that calls one without a request has to
    ///     establish it: <c>Case</c> is an <c>ITenantEntity</c>, so with no tenant resolved the query
    ///     filter is <b>fail-closed</b> and a row that was just written is not there — measured as a 404
    ///     from an update of a case created two lines above. This is the shared-schema tenant a test
    ///     writes in unless it onboards an organisation of its own.
    /// </remarks>
    protected const string Tenant = "casework-tests";


    /// <summary>Runs <paramref name="work" /> as the tenant above.</summary>
    protected static async Task<T> AsTenantAsync<T>(Func<Task<T>> work, string tenant = Tenant)
    {
        using var scope = TenantScope.BeginScope(tenant);

        return await work();
    }

    /// <summary>The tables of one of the two databases, read with SQL.</summary>
    protected static async Task<List<string>> TablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "select table_name from information_schema.tables where table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();

        var tables = new List<string>();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        return tables;
    }

    /// <summary>One scalar out of one of the databases: the shortest way to read what another process wrote.</summary>
    protected static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    ///     The response body as JSON, or a failure that carries the body: a test that fails saying only
    ///     "500" has to be run again to learn why.
    /// </summary>
    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode} {response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();

        await _intake.DisposeAsync();
        await _verify.DisposeAsync();
    }
}
