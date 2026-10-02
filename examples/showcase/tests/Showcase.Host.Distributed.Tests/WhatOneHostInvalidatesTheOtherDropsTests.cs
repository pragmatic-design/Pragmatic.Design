using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     <c>Pragmatic.Caching.Redis</c>: what one host invalidates, every host with the
///     broadcast drops.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The package does not share the cached values</b>, and the issue's sketch said it did
///         ("a value cached by one host is served from the other"). Its own summary is the correction:
///         "what one node invalidates, every node drops" — a distributed cache behind
///         <c>HybridCache</c> shares the entries; it does not share the invalidations, and each host's
///         in-process copy outlives an invalidation run anywhere else. So what is asserted here is
///         that the first host's own copy is <b>gone</b> after the second host writes.
///     </para>
///     <para>
///         This is the only topology in the repository where that can be shown. <c>[Cacheable]</c> is
///         declared five times in Showcase and the monolith has one host, where the invalidation and
///         the copy live in the same process and the broadcast changes nothing.
///     </para>
///     <para>
///         ⚠️ The row is renamed <b>through the database</b>, not through the mutation. It has to be:
///         the mutation is what invalidates, so using it would leave "the copy is live" racing against
///         the very broadcast under test. Writing past it is what makes a cached answer
///         distinguishable from a fresh one at all.
///     </para>
/// </remarks>
[Collection(SharedCacheCollection.Name)]
public sealed class WhatOneHostInvalidatesTheOtherDropsTests(SharedCacheFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     The hosts that declare the broadcast have it, and the control does not — asserted on the
    ///     registration, because everything below is about what that decorator does.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Kept as a case of its own after the first run of the case below failed on the broadcast
    ///     never arriving: "the value did not change" has two causes — a channel that does not carry
    ///     and a decorator that was never registered — and they are not the same finding.
    /// </remarks>
    [Fact]
    public void TheHostsThatDeclareIt_HaveTheBroadcast()
    {
        StackOf(fixture.First).Should().Contain("Broadcasting",
            "ConnectionStrings:Redis is set for this host, so Program.cs decorates its cache stack");
        StackOf(fixture.Second).Should().Contain("Broadcasting");

        StackOf(fixture.Alone).Should().NotContain("Broadcasting",
            "the control declares no Redis, and without the call nothing changes — the stack does not "
            + "look for a broadcast at runtime");
    }

    private static string StackOf(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Pragmatic.Caching.ICacheStack>().GetType().Name;
    }

    /// <summary>
    ///     The flags of this host come from configuration, not from a seeder.
    /// </summary>
    /// <remarks>
    ///     The monolith seeds them programmatically into the in-memory store on startup, which its own
    ///     comment calls a demo. This host reads the <c>FeatureFlags</c> section, so an operator turns
    ///     one on without a deployment.
    /// </remarks>
    /// <summary>
    ///     This host publishes its topology through the Agent, not into its own memory.
    /// </summary>
    /// <remarks>
    ///     The generated composition calls <c>AddDiscovery()</c>, whose backend keeps the topology in
    ///     process — which in a topology with two hosts means each one knows only itself.
    ///     <c>UseAgentDiscovery()</c> replaces the backend, and replacing is all the package does:
    ///     what the backend then stores is <c>AgentDiscoveryBackend</c>'s own business, tested where
    ///     it lives, and it needs a daemon this suite has none of.
    /// </remarks>
    [Fact]
    public void TheTopologyIsPublishedThroughTheAgent()
    {
        using var scope = fixture.First.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<Pragmatic.Discovery.Abstractions.IDiscoveryBackend>()
            .GetType().Name
            .Should().Be("AgentDiscoveryBackend",
                "Program.cs declares it, and UseAgentDiscovery removes the default before adding — so "
                + "it cannot lose to what the composition registered first");
    }

    [Fact]
    public void TheFlagsComeFromConfiguration()
    {
        using var scope = fixture.First.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<Pragmatic.FeatureFlags.IFeatureFlagStore>()
            .GetType().Name
            .Should().Be("ConfigurationFeatureFlagStore",
                "Program.cs declares the configuration store, and a host that declares one must get it");
    }

    [Fact]
    public async Task AWriteOnOneHost_DropsTheCachedAnswerOnTheOther()
    {
        using var first = SharedCacheFixture.ClientOf(fixture.First);
        using var second = SharedCacheFixture.ClientOf(fixture.Second);
        using var alone = SharedCacheFixture.ClientOf(fixture.Alone);

        var before = $"Before-{Guid.NewGuid():N}"[..20];
        var after = $"After-{Guid.NewGuid():N}"[..20];
        var propertyId = await APropertyAsync(second, before);

        // Each host reads it and holds its own copy — the control included.
        (await SearchAsync(first)).Should().Contain(before, "the first host reads it and caches it");
        (await SearchAsync(alone)).Should().Contain(before, "and so does the control");

        // Past the mutation, so nothing is invalidated and nothing races.
        await RenameInTheDatabaseAsync(propertyId, after);

        (await SearchAsync(first)).Should().Contain(before,
            "the copy is live: the row changed and this host has not noticed, which is what makes "
            + "the assertion below about the broadcast and not about a cache that never held anything");
        (await SearchAsync(alone)).Should().Contain(before, "the control's copy is live too");

        // The write that invalidates, on the other host. Its own copy goes locally; reaching this
        // host's copy is the broadcast's job.
        await TouchAsync(second, propertyId, after);

        (await EventuallySeesAsync(first, after)).Should().BeTrue(
            "the second host's invalidation reached the first host's own copy, which is what "
            + "AddRedisCacheInvalidationBroadcast is for");

        (await SearchAsync(alone)).Should().Contain(before,
            "and the control, which declares no broadcast, is still serving the old answer — that is "
            + "what a second host does without the package, for the whole five minutes of the entry");
    }

    /// <summary>Creates a property and returns its id.</summary>
    private static async Task<Guid> APropertyAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(new Uri("api/properties", UriKind.Relative), new
        {
            code = $"SC-{Guid.NewGuid():N}"[..12],
            name,
            city = "Rome",
            country = "IT",
            starRating = 4
        });

        response.IsSuccessStatusCode.Should().BeTrue(
            "the case needs a row to cache. Got {0}: {1}",
            response.StatusCode, await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>Renames the row without going through the mutation that invalidates.</summary>
    private async Task RenameInTheDatabaseAsync(Guid id, string name)
    {
        using var scope = fixture.Second.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        var changed = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.PersistenceId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, name));

        changed.Should().Be(1, "the rename has to land, or nothing below is measuring a cache");
    }

    /// <summary>A write through the mutation: it is what invalidates, and what broadcasts.</summary>
    private static async Task TouchAsync(HttpClient client, Guid id, string name)
    {
        var response = await client.PutAsJsonAsync(new Uri($"api/properties/{id}", UriKind.Relative), new
        {
            id,
            name
        });

        response.IsSuccessStatusCode.Should().BeTrue(
            "the write is what invalidates. Got {0}: {1}",
            response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     Polls until the host produces the new answer. The broadcast is pub/sub, so "at once" is not
    ///     a property it has — but never arriving is a failure, and that is what the timeout says.
    /// </summary>
    private static async Task<bool> EventuallySeesAsync(HttpClient client, string name)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if ((await SearchAsync(client)).Contains(name, StringComparison.Ordinal))
                return true;

            await Task.Delay(250);
        }

        return false;
    }

    private static async Task<string> SearchAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("api/properties/search", UriKind.Relative));

        response.IsSuccessStatusCode.Should().BeTrue(
            "the search is the cached read this case is about. Got {0}", response.StatusCode);

        return await response.Content.ReadAsStringAsync();
    }
}
