using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Identity.Local.Services;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Boots the real Time off host against the test database.
/// </summary>
/// <remarks>
///     <para>
///         Configuration is supplied, and the mailbox replaced — the invitations land where the test
///         can read them, as a person would; an interceptor records the SQL sent, and changes none of it. Everything else — the generated wiring, the endpoints, the
///         pipeline — runs as it does in production. A test that passed on a doctored host would prove
///         nothing about the example.
///     </para>
///     <para>
///         Not Development: the committed development settings stay out of the tests, and the token
///         validation runs as strict as it does in production — issuer and audience required.
///         Settings go through <c>UseSetting</c> because <c>Program.cs</c> reads the signing key while
///         it registers the services, before configuration added later would be visible.
///     </para>
/// </remarks>
public sealed class TimeOffWebFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? services = null)
    : WebApplicationFactory<Program>
{
    // The registry's keys, one pair for the whole run: the hosts share a database, and in production the
    // keys outlive a restart — a new pair per host would give the same employee a new reference in each.
    private static readonly string IdentityKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string LookupKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public TestMailbox Mailbox { get; } = new();

    public SqlCapture Sql { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:App", connectionString);
        builder.UseSetting("Jwt:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Privacy:IdentityKey", IdentityKey);
        builder.UseSetting("Privacy:LookupKey", LookupKey);
        builder.UseSetting("TimeOff:FirstAdministrator:FullName", TestAccounts.FirstAdministrator.FullName);
        builder.UseSetting("TimeOff:FirstAdministrator:WorkEmail", TestAccounts.FirstAdministrator.WorkEmail);
        builder.UseSetting("TimeOff:FirstAdministrator:Password", TestAccounts.FirstAdministrator.Password);

        // A host that fails to start must fail the test, not answer 503 from maintenance mode.
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        // The sign-in limit is per client address, and under TestServer there is none: every request of a
        // class shares one bucket, and a class signs in far more than a person does in a minute. Raised
        // for the suite; SigningInFromOneAddress measures it with a limit of its own.
        builder.UseSetting("Identity:Local:RateLimit:PermitLimit", "100000");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(replaced =>
        {
            replaced.AddSingleton<IPasswordResetNotifier>(Mailbox);
            replaced.AddSingleton<IInterceptor>(Sql);
            services?.Invoke(replaced);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
    }
}
