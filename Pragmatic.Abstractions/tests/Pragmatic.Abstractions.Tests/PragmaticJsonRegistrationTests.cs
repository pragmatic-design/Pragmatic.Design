using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Serialization;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class PragmaticJsonRegistrationTests
{
    [Fact]
    public void AddPragmaticJson_CalledTwice_RegistersOneSharedSingleton()
    {
        var services = new ServiceCollection();

        services.AddPragmaticJson();
        services.AddPragmaticJson();

        services.Count(d => d.ServiceType == typeof(PragmaticJsonOptions)).Should().Be(1);
    }

    [Fact]
    public void AddPragmaticJsonContext_SameInstanceTwice_IsDeduplicated()
    {
        var services = new ServiceCollection();
        var context = PragmaticCommonJsonContext.Default;

        services.AddPragmaticJsonContext(context);
        services.AddPragmaticJsonContext(context);

        var options = (PragmaticJsonOptions)services
            .Single(d => d.ServiceType == typeof(PragmaticJsonOptions)).ImplementationInstance!;
        options.Contexts.Count(c => ReferenceEquals(c, context)).Should().Be(1);
    }

    /// <summary>
    ///     A modifier contributed by a package reaches the instance the host resolves and builds from.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The obvious registration is the wrong one. <see cref="PragmaticJsonOptions"/> is a singleton
    ///     <b>instance</b>, so <c>services.Configure&lt;PragmaticJsonOptions&gt;(...)</c> compiles, reads
    ///     correctly, and never runs — nothing resolves <c>IOptions&lt;PragmaticJsonOptions&gt;</c>. The
    ///     symptom is a payload that is well formed and unconverted, which no exception describes.
    /// </remarks>
    [Fact]
    public void AddPragmaticJsonModifier_ReachesTheInstanceTheHostResolves()
    {
        var services = new ServiceCollection();
        static void Modifier(JsonTypeInfo typeInfo) { }

        services.AddPragmaticJsonModifier(Modifier);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PragmaticJsonOptions>().Modifiers
            .Should().Contain((Action<JsonTypeInfo>)Modifier);
    }

    /// <summary>
    ///     The control: the options pattern does not reach it, which is why the registration above exists.
    /// </summary>
    [Fact]
    public void Configure_OnTheSharedOptions_NeverRuns()
    {
        var services = new ServiceCollection();
        services.AddPragmaticJson();
        services.Configure<PragmaticJsonOptions>(o => o.AddModifier(_ => { }));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PragmaticJsonOptions>().Modifiers
            .Should().BeEmpty("the seam is an instance, not an options-pattern type");
    }

    [Fact]
    public void GetOrAddOptions_ModuleAndHost_ObserveTheSameInstance()
    {
        var services = new ServiceCollection();

        services.AddPragmaticJson();                                  // module registration
        var first = (PragmaticJsonOptions)services
            .Single(d => d.ServiceType == typeof(PragmaticJsonOptions)).ImplementationInstance!;

        services.AddPragmaticJsonContext(PragmaticCommonJsonContext.Default);  // later contribution
        var second = (PragmaticJsonOptions)services
            .Single(d => d.ServiceType == typeof(PragmaticJsonOptions)).ImplementationInstance!;

        second.Should().BeSameAs(first, "host configuration and module contributions must share one seam");
    }
}
