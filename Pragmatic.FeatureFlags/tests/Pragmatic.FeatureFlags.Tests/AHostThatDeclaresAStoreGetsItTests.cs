using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags.Configuration;
using Pragmatic.FeatureFlags.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests;

/// <summary>
///     A host that declares a store gets it, whatever the composition registered first.
/// </summary>
/// <remarks>
///     <para>
///         The generated entry point calls <c>RegisterAllPragmaticServices</c> <b>before</b> it invokes
///         the host's callback, so by the time an application says which store it wants, the framework
///         default has taken the slot. A <c>TryAdd</c> against a taken slot is silence:
///         <c>AddConfigurationFeatureFlagStore()</c> — the only registration that package ships, and
///         the one its documentation tells you to call — left the host with the in-memory store and
///         said nothing.
///     </para>
///     <para>
///         ⚠️ Measured on <c>Showcase.Host.Distributed</c>, where the example had to carry
///         <c>RemoveAll&lt;IFeatureFlagStore&gt;()</c> before the documented call. The rule it broke is
///         "decide at compile time": the module declares and the composition follows — it does not silently outvote
///         the declaration.
///     </para>
///     <para>
///         ⚠️ The parameterless <c>AddPragmaticFeatureFlags()</c> keeps its <c>TryAdd</c>, and that is
///         not the same thing: it <b>is</b> the default, and its own summary says a store registered
///         before it wins. A default that yields is correct; a caller's explicit choice that yields is
///         the defect.
///     </para>
/// </remarks>
public class AHostThatDeclaresAStoreGetsItTests
{
    private static ServiceProvider Composed(Action<IServiceCollection> hostCallback)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FeatureFlags:beta"] = "true" })
            .Build());

        // The order the generated entry point produces: the framework composes, then the host speaks.
        services.AddPragmaticFeatureFlags();
        hostCallback(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheConfigurationStore_WinsOverTheFrameworkDefault()
    {
        using var provider = Composed(services => services.AddConfigurationFeatureFlagStore());

        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<ConfigurationFeatureFlagStore>(
            "the host asked for it after the composition ran, which is the only moment a host gets");
    }

    [Fact]
    public async Task AndTheFlagsComeFromConfiguration()
    {
        using var provider = Composed(services => services.AddConfigurationFeatureFlagStore());

        (await provider.GetRequiredService<IFeatureFlagStore>().IsEnabledAsync("beta"))
            .Should().BeTrue("the section says so, and it is the section's store that answers");
    }

    /// <summary>
    ///     The typed overload is a caller's choice too, and loses to the default the same way.
    /// </summary>
    /// <remarks>
    ///     Item 2 of the issue, and the answer is yes: with the parameterless overload called first —
    ///     which is what the generated composition does — <c>TryAddSingleton&lt;IFeatureFlagStore,
    ///     TStore&gt;()</c> never takes.
    /// </remarks>
    [Fact]
    public void TheTypedOverload_WinsOverTheFrameworkDefault()
    {
        using var provider = Composed(services => services.AddPragmaticFeatureFlags<ConfigurationFeatureFlagStore>());

        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<ConfigurationFeatureFlagStore>();
    }

    /// <summary>
    ///     The control: the default is still the default when nobody asks for anything.
    /// </summary>
    /// <remarks>
    ///     Without it, "the declared store wins" is satisfied by never registering the in-memory one —
    ///     and an application that asks for no store must still have one.
    /// </remarks>
    [Fact]
    public void WithoutADeclaration_TheInMemoryStoreStands()
    {
        using var provider = Composed(_ => { });

        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<InMemoryFeatureFlagStore>();
    }

    /// <summary>
    ///     The other control: a store registered <b>before</b> the framework still wins, which is what
    ///     the parameterless overload's own summary promises.
    /// </summary>
    /// <remarks>
    ///     This is the half that must keep working, and the reason "make everything replace" is the
    ///     wrong rule: the default yielding to a prior registration is the documented contract.
    /// </remarks>
    [Fact]
    public void AStoreRegisteredFirst_IsNotOverwrittenByTheDefault()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddConfigurationFeatureFlagStore();
        services.AddPragmaticFeatureFlags();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<ConfigurationFeatureFlagStore>();
    }

    /// <summary>
    ///     Declaring a store twice leaves one, not a pile.
    /// </summary>
    /// <remarks>
    ///     A replace that appended would leave the container resolving the last and holding the rest —
    ///     invisible until something enumerates <c>IEnumerable&lt;IFeatureFlagStore&gt;</c>.
    /// </remarks>
    [Fact]
    public void DeclaringItTwice_LeavesOneRegistration()
    {
        using var provider = Composed(services =>
        {
            services.AddConfigurationFeatureFlagStore();
            services.AddConfigurationFeatureFlagStore();
        });

        provider.GetServices<IFeatureFlagStore>().Should().HaveCount(1);
    }
}
