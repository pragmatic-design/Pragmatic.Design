using Casework.Verify.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     Boots the real Verify host against its own test database.
/// </summary>
/// <remarks>
///     The same shape as <see cref="IntakeWebFactory" />, and deliberately a second class rather than one
///     factory with a switch: the two processes are configured by different keys, and the day one of them
///     needs something the other must not have, a parameter would be the wrong place to find out.
/// </remarks>
public sealed class VerifyWebFactory(
    string connectionString,
    string brokerConnectionString,
    string tenantConnectionStringTemplate,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? services = null)
    : WebApplicationFactory<VerifyHost>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Verify", connectionString);
        builder.UseSetting("Messaging:RabbitMq:ConnectionString", brokerConnectionString);
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        // Where an organisation with a database of its own lives in **this** service.
        builder.UseSetting("MultiTenancy:ConnectionStringTemplate", tenantConnectionStringTemplate);

        // The service token this host accepts. ⚠️ Its own issuer and audience, not Intake's:
        // the two services do not accept each other's tokens, and a test that shared them would hide the
        // day one of them stops trusting the other.
        builder.UseSetting("Jwt:Key", TestTokens.SigningKey);
        builder.UseSetting("Jwt:Issuer", TestTokens.VerifyIssuer);
        builder.UseSetting("Jwt:Audience", TestTokens.VerifyIssuer);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(replaced => services?.Invoke(replaced));

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
    }
}
