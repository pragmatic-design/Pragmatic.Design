using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Email;
using Pragmatic.Email.Builder;
using Pragmatic.Email.Extensions;
using Pragmatic.Email.Testing;

namespace Pragmatic.Email.Samples.Samples;

/// <summary>
///     Host integration via <see cref="PragmaticBuilderEmailExtensions.UseEmail"/> on an
///     <see cref="IPragmaticBuilder"/>. In a real app the Pragmatic host provides the
///     builder; here a minimal one wraps a <see cref="ServiceCollection"/> so the wiring
///     stays self-contained. The <c>EmailBuilder</c> callback is the same one used by
///     <c>AddPragmaticEmail</c>.
/// </summary>
public static class PragmaticHostIntegrationSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- PragmaticHost integration (UseEmail) ---");

        var services = new ServiceCollection();
        services.AddLogging();

        IPragmaticBuilder builder = new SampleBuilder(services);

        // The module surfaces UseEmail on IPragmaticBuilder; the callback is the EmailBuilder.
        builder.UseEmail(email => email.UseNullTransport());

        var harness = services.AddEmailTestHarness();

        await using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<IEmailSender>();

        var message = new EmailMessageBuilder()
            .From("host@example.com")
            .To("user@example.com")
            .Subject("Configured via UseEmail")
            .TextBody("Wired through IPragmaticBuilder.UseEmail.")
            .Build();

        var result = await sender.SendAsync(message);

        Console.WriteLine($"  send succeeded   : {result.Success}");
        Console.WriteLine($"  harness recorded : {harness.Sent.Count} message(s)");
        Console.WriteLine();
    }

    /// <summary>Minimal <see cref="IPragmaticBuilder"/> standing in for the real host builder.</summary>
    private sealed class SampleBuilder(IServiceCollection services) : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = services;
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new SampleEnvironment();

        private sealed class SampleEnvironment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "Pragmatic.Email.Samples";
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
                new Microsoft.Extensions.FileProviders.NullFileProvider();
        }
    }
}
