using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.FeatureFlags.Providers;
using Xunit;

namespace Pragmatic.FeatureFlags.Tests;

public class GenericFeatureFlagStoreRegistrationTests
{
    [Fact]
    public void AddPragmaticFeatureFlagsOfTStore_RegistersCustomStore()
    {
        var services = new ServiceCollection();

        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IFeatureFlagStore>().Should().BeOfType<FakeFeatureFlagStore>();
    }

    [Fact]
    public async Task AddPragmaticFeatureFlagsOfTStore_ResolvedStoreIsUsable()
    {
        var services = new ServiceCollection();

        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IFeatureFlagStore>();

        (await store.IsEnabledAsync("anything")).Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticFeatureFlagsOfTStore_IsIdempotent_LeavesOneRegistration()
    {
        var services = new ServiceCollection();

        // Both calls replace, so the second removes the first and only one descriptor for
        // IFeatureFlagStore exists.
        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();
        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IFeatureFlagStore>().Should().ContainSingle();
    }

    /// <summary>
    ///     Naming a store after the default has been registered gets you that store.
    /// </summary>
    /// <remarks>
    ///     ⚠️ "The later generic TryAddSingleton is a no-op: the in-memory store wins" describes a
    ///     mechanism, not the rule, and here it would be the defect: the generated entry point registers
    ///     the framework's services <b>before</b> it invokes the host's callback, so this order is the
    ///     only order a host ever gets, and a caller naming a store would never get it. The
    ///     parameterless overload keeps its <c>TryAdd</c> — it is the default, and yielding to a prior registration is its
    ///     documented contract, which the case below still holds.
    /// </remarks>
    [Fact]
    public void AddPragmaticFeatureFlagsOfTStore_AfterDefault_TakesTheNamedStore()
    {
        var services = new ServiceCollection();

        services.AddPragmaticFeatureFlags();
        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IFeatureFlagStore>().Should().ContainSingle(
            "replacing leaves one registration, not a pile the container resolves the last of");
        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<FakeFeatureFlagStore>();
    }

    /// <summary>
    ///     The control the change must not break: the default still yields to a store registered first.
    /// </summary>
    /// <remarks>
    ///     <c>AddPragmaticFeatureFlags()</c>'s own summary promises it — "the store can be replaced by
    ///     registering a custom <c>IFeatureFlagStore</c> before calling this" — so that overload keeps
    ///     its <c>TryAdd</c>. Without this case, "the named store wins" would be satisfied by making
    ///     every registration replace, and the default would overwrite the caller in the other order.
    /// </remarks>
    [Fact]
    public void TheDefault_StillYieldsToAStoreRegisteredBeforeIt()
    {
        var services = new ServiceCollection();

        services.AddPragmaticFeatureFlags<FakeFeatureFlagStore>();
        services.AddPragmaticFeatureFlags();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFeatureFlagStore>().Should().BeOfType<FakeFeatureFlagStore>();
    }

    private sealed class FakeFeatureFlagStore : IFeatureFlagStore
    {
        public Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default) =>
            Task.FromResult<FeatureFlagDefinition?>(new FeatureFlagDefinition { Name = flagName, Enabled = true });

        public Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FeatureFlagDefinition>>([]);

        public async IAsyncEnumerable<FeatureFlagChange> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
