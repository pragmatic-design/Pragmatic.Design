// Pragmatic.Discovery Samples - DiscoveryOptions configuration via appsettings.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Discovery.Extensions;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Samples;

/// <summary>
/// Demonstrates binding <see cref="DiscoveryOptions"/> from configuration (appsettings.json shape)
/// using <see cref="DiscoveryOptions.SectionName"/>, and resolving the bound options.
/// </summary>
internal static class OptionsConfigurationSample
{
    public static void Run()
    {
        SampleConsole.Header("DiscoveryOptions — Configuration via appsettings");

        // Simulate the contents of appsettings.json under the "Pragmatic:Discovery" section.
        var appsettings = new Dictionary<string, string?>
        {
            ["Pragmatic:Discovery:AutoRegisterOnStartup"] = "true",
            ["Pragmatic:Discovery:ValidateOnStartup"] = "true",
            ["Pragmatic:Discovery:ThrowOnValidationFailure"] = "true",
        };

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(appsettings)
            .Build();

        SampleConsole.Step($"Binding section \"{DiscoveryOptions.SectionName}\" from configuration.");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDiscovery();
        // Bind options from the configuration section (idiomatic appsettings wiring).
        services.Configure<DiscoveryOptions>(config.GetSection(DiscoveryOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value;

        SampleConsole.Info($"AutoRegisterOnStartup   = {opts.AutoRegisterOnStartup}");
        SampleConsole.Info($"ValidateOnStartup       = {opts.ValidateOnStartup}");
        SampleConsole.Info($"ThrowOnValidationFailure = {opts.ThrowOnValidationFailure}");

        SampleConsole.Blank();
    }
}
