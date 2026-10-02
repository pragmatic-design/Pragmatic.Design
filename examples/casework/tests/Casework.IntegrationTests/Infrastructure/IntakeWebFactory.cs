using Casework.Intake.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     Boots the real Intake host against the test database.
/// </summary>
/// <remarks>
///     <para>
///         Configuration is supplied; everything else — the generated wiring, the endpoints, the pipeline
///         — runs as it does in production. A test that passed on a doctored host would prove nothing
///         about the example.
///     </para>
///     <para>
///         The type argument is <see cref="IntakeHost" />, a public marker in the host's assembly, and not
///         <c>Program</c>: with two hosts referenced by one test project, <c>Program</c> — which top-level
///         statements put in the global namespace — names either nothing or the wrong process.
///     </para>
///     <para>
///         Not Development: the committed development settings stay out of the tests, and a token is
///         validated as strictly as it is in production. Settings go through <c>UseSetting</c> because
///         <c>Program.cs</c> reads configuration while it registers the services, before configuration
///         added later would be visible.
///     </para>
/// </remarks>
public sealed class IntakeWebFactory(
    string connectionString,
    string brokerConnectionString,
    string tenantConnectionStringTemplate,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? services = null)
    : WebApplicationFactory<IntakeHost>
{
    /// <summary>
    ///     Where this host's documents are written: a directory of this run's own.
    /// </summary>
    /// <remarks>
    ///     Not the application's default, which is <c>AppContext.BaseDirectory/storage</c> — that is
    ///     inside <c>bin/</c>, so a suite that uploaded anything would leave files in the build output
    ///     and the next run would start with the previous one's. Removed with the host, so a green run
    ///     leaves nothing behind; kept if the removal fails, because a test's cleanup must not fail the
    ///     test.
    /// </remarks>
    public string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(), "casework-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Intake", connectionString);
        builder.UseSetting("Storage:Root", StorageRoot);

        // Where an organisation with a database of its own lives. The host interpolates
        // the tenant id into it; the databases do not exist until the framework provisions one.
        builder.UseSetting("MultiTenancy:ConnectionStringTemplate", tenantConnectionStringTemplate);

        // The broker both services meet on. Through UseSetting for the same reason as the rest:
        // UseMessaging reads it while it registers the transport.
        builder.UseSetting("Messaging:RabbitMq:ConnectionString", brokerConnectionString);

        // The signing key of the tokens this service issues and validates. Through UseSetting, because
        // UseJwtAuthentication reads the section while it registers the services.
        builder.UseSetting("Jwt:Key", TestTokens.SigningKey);
        builder.UseSetting("Jwt:Issuer", TestTokens.Issuer);
        builder.UseSetting("Jwt:Audience", TestTokens.Audience);

        // A host that fails to start must fail the test, not answer 503 from maintenance mode.
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(replaced => services?.Invoke(replaced));

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        try
        {
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open loses the suite a temporary directory, and that is all: failing the
            // run over the cleanup of a run that passed would be the wrong trade.
        }
    }
}
