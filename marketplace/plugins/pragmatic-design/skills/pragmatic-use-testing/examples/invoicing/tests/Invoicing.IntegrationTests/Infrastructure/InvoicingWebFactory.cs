using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Pragmatic.Storage;
using Pragmatic.Storage.InMemory;

namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     Boots the real Invoicing host against the test database.
/// </summary>
/// <remarks>
///     <para>
///         Configuration is supplied; everything else — the generated wiring, the endpoints, the
///         pipeline — runs as it does in production. A test that passed on a doctored host would prove
///         nothing about the example.
///     </para>
///     <para>
///         Not Development: the committed development settings stay out of the tests, and a token is
///         validated as strictly as it is in production. Settings go through <c>UseSetting</c> because
///         <c>Program.cs</c> reads configuration while it registers the services, before configuration
///         added later would be visible.
///     </para>
/// </remarks>
public sealed class InvoicingWebFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? services = null)
    : WebApplicationFactory<Program>
{
    /// <summary>The statements the application sent to the database.</summary>
    public SqlCapture Sql { get; } = new();

    /// <summary>
    ///     Where the issued documents go in a test run: memory, not the disk.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every assertion about a document goes through <c>IFileStorage</c> — the
    ///         reminder reads back the PDF the issue stored and compares its hash — so the double
    ///         serves them all unchanged, and the suite leaves no folder of PDFs in the
    ///         temporary directory of whoever ran it: a disk store there would be created
    ///         per run and removed by nothing.
    ///     </para>
    ///     <para>
    ///         It also answers something the disk could not be asked without knowing the layout:
    ///         <see cref="InMemoryFileStorage.Count" /> — how many objects the application actually
    ///         stored, which is how "the reminder attaches the document that was issued" gains its
    ///         other half, "and did not store a second one".
    ///     </para>
    ///     <para>
    ///         ⚠️ Registered for <c>IFileInfoProvider</c> too, the second interface it implements:
    ///         replacing one of the two would leave a host that asks for the other reading a disk
    ///         nobody wrote to.
    ///     </para>
    /// </remarks>
    public InMemoryFileStorage Files { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:App", connectionString);

        // The provider the tokens come from. Through UseSetting, because Program.cs reads these two keys
        // while it registers the services — configuration added later would not be visible to it.
        builder.UseSetting("Oidc:Authority", TestIdentityProvider.Authority);
        builder.UseSetting("Oidc:Audience", TestIdentityProvider.Audience);

        // No Storage:Root: the disk store is replaced below, and app.UseStorage registers a factory,
        // so with the replacement in place it is never constructed and no folder is created.

        // A host that fails to start must fail the test, not answer 503 from maintenance mode.
        builder.UseSetting("Pragmatic:MaintenanceMode:EnableOnStartupFailure", "false");

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(replaced =>
        {
            TestIdentityProvider.Configure(replaced);

            // What the reads cost: the application's contexts take their interceptors from the container,
            // so this sees every statement without the host knowing it is there.
            replaced.AddSingleton<IInterceptor>(Sql);

            // The local double in place of the disk. See the remark on Files.
            replaced.Replace(ServiceDescriptor.Singleton<IFileStorage>(Files));
            replaced.Replace(ServiceDescriptor.Singleton<IFileInfoProvider>(Files));

            services?.Invoke(replaced);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
    }
}
