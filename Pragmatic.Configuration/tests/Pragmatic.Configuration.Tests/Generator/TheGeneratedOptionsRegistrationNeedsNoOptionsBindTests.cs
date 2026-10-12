using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
///     The generated <c>Add{Type}Options</c> binds <c>IOptions&lt;T&gt;</c> without <c>OptionsBuilder.Bind</c>:
///     through the generated binder when the class has one, through <c>ConfigurationBinder.Bind</c> when it does
///     not, and <c>IOptionsMonitor&lt;T&gt;</c> still sees a reloaded value (#195).
/// </summary>
/// <remarks>
///     <c>OptionsBuilder.Bind</c> binds by reflection and lives in <c>Microsoft.Extensions.Options.ConfigurationExtensions</c>,
///     which Pragmatic.Configuration does not depend on: a host without the ASP.NET Core shared framework did not
///     compile the registration. The compilation here has only the module's own dependencies
///     (<see cref="ConfigurationGeneratorTestBase.RunGeneratorToCompile" />).
/// </remarks>
public class TheGeneratedOptionsRegistrationNeedsNoOptionsBindTests : ConfigurationGeneratorTestBase
{
    private const string Source = """
        using System.Collections.Generic;
        using Pragmatic.Configuration;

        namespace TestApp;

        [Configuration]
        public partial class BillingOptions
        {
            public string Currency { get; set; } = "";
            public decimal TaxRate { get; set; }
        }

        [Configuration]
        public partial class RegionsOptions
        {
            public List<string> Names { get; set; } = new();
        }
        """;

    private static readonly Lazy<Assembly> Generated = new(Compile);

    [Fact]
    public void TheRegistration_CallsNoOptionsBind()
    {
        var result = RunGeneratorToCompile(Source);

        GetGeneratedSource(result, "BillingOptions.Registration").Should()
            .Contain("BillingOptionsTenantBinder().Bind(").And.NotContain(".Bind(configuration");
        GetGeneratedSource(result, "RegionsOptions.Registration").Should()
            .Contain("ConfigurationBinder.Bind(").And.NotContain(".Bind(configuration");
    }

    [Fact]
    public void AClassWithABinder_IsBoundAndReloads()
    {
        var root = Configuration(("Billing:Currency", "EUR"), ("Billing:TaxRate", "0.22"));
        var monitor = Monitor("TestApp.BillingOptions", "AddBillingOptions", root);

        Read(monitor, "Currency").Should().Be("EUR");
        Read(monitor, "TaxRate").Should().Be(0.22m);

        root["Billing:Currency"] = "USD";
        root.Reload();

        Read(monitor, "Currency").Should().Be("USD", "IOptionsMonitor follows the section's reload token");
    }

    [Fact]
    public void AClassWithoutABinder_IsBoundAndReloads()
    {
        var root = Configuration(("Regions:Names:0", "eu"), ("Regions:Names:1", "us"));
        var monitor = Monitor("TestApp.RegionsOptions", "AddRegionsOptions", root);

        ((List<string>)Read(monitor, "Names")!).Should().Equal("eu", "us");

        root["Regions:Names:2"] = "ap";
        root.Reload();

        ((List<string>)Read(monitor, "Names")!).Should().Equal("eu", "us", "ap");
    }

    private static IConfigurationRoot Configuration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static object Monitor(string typeName, string addMethod, IConfiguration configuration)
    {
        var assembly = Generated.Value;
        var options = assembly.GetType(typeName)!;
        var add = assembly.GetTypes().Select(t => t.GetMethod(addMethod, BindingFlags.Public | BindingFlags.Static))
            .Single(m => m is not null)!;

        var services = new ServiceCollection();
        add.Invoke(null, [services, configuration]);
        var provider = services.BuildServiceProvider();

        return provider.GetRequiredService(typeof(IOptionsMonitor<>).MakeGenericType(options));
    }

    private static object? Read(object monitor, string property)
    {
        var current = monitor.GetType().GetProperty("CurrentValue")!.GetValue(monitor)!;
        return current.GetType().GetProperty(property)!.GetValue(current);
    }

    private static Assembly Compile()
    {
        var result = RunGeneratorToCompile(Source);
        GetCompilationErrors(result).Should().BeEmpty("the registration compiles with only the module's own dependencies");

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue("emit must succeed: " + string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())));

        return Assembly.Load(stream.ToArray());
    }
}
