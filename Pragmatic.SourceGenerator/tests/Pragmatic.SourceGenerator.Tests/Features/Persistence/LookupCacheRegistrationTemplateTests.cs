using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class LookupCacheRegistrationTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesRegistrationMethod()
    {
        var lookups = BuildLookups();
        var source = Render(lookups);

        source.Should().Contain("AddSalesLookupCaches(");
        source.Should().Contain("IServiceCollection services");
        source.Should().Contain("return services;");
    }

    [Fact]
    public void RenderOutput_RegistersCacheAsSingleton()
    {
        var lookups = BuildLookups();
        var source = Render(lookups);

        source.Should().Contain("TryAddSingleton<global::Pragmatic.Persistence.Entity.ILookupCache<global::Sales.Category, global::Guid>>(services, "
            + "new global::Pragmatic.Persistence.Caching.LookupCache<global::Sales.Category, global::Guid>())");
    }

    /// <summary>
    ///     Every registration is a TryAdd: on the engines each boundary's <c>Add{Boundary}Persistence</c> calls the method (#881), and a
    ///     second loader would reload every lookup write twice.
    /// </summary>
    [Fact]
    public void RenderOutput_RegistersNothingTwice()
    {
        var source = Render(BuildLookups());

        source.Should().NotContain("services.AddSingleton<");
        source.Should().Contain("ServiceDescriptor.Singleton<global::Pragmatic.Persistence.Caching.ILookupCacheLoader, global::Sales.CategoryLookupCacheLoader>()");
    }

    [Fact]
    public void RenderOutput_MultipleLookups_RegistersAll()
    {
        var lookups = ImmutableArray.Create(
            BuildEntity("Sales", "Category", "Guid"),
            BuildEntity("Sales", "Country", "int"));
        var source = Render(lookups);

        source.Should().Contain("ILookupCache<global::Sales.Category, global::Guid>");
        source.Should().Contain("ILookupCache<global::Sales.Country, ");
    }

    /// <summary>
    ///     A tenant-scoped lookup gets the observer that keeps its caches in step with the tenants.
    /// </summary>
    /// <remarks>
    ///     The startup preload enumerates the tenants known then and stops. Without the observer a
    ///     tenant created afterwards has no cache, and every navigation over that lookup throws for it
    ///     until the host restarts.
    /// </remarks>
    [Fact]
    public void ATenantScopedLookup_RegistersTheTenantLifecycleObserver()
    {
        var source = Render(ImmutableArray.Create(TenantScoped(BuildEntity("Sales", "Region", "int"))));

        source.Should().Contain(
            "ServiceDescriptor.Singleton<global::Pragmatic.MultiTenancy.ITenantLifecycleObserver, "
            + "global::Pragmatic.Persistence.Caching.LookupCacheTenantObserver>());");
    }

    /// <summary>
    ///     The reload after a committed write is a commit observer, which EF Core's context and the engines' sessions both call (#881), and
    ///     no EF Core interceptor.
    /// </summary>
    [Fact]
    public void TheReload_IsACommitObserver_NotAnEfCoreInterceptor()
    {
        var source = Render(BuildLookups());

        source.Should().Contain("ServiceDescriptor.Singleton<global::Pragmatic.Persistence.Repository.ICommitObserver, "
            + "global::Pragmatic.Persistence.Caching.LookupCacheReloadObserver>()");
        source.Should().NotContain("EntityFrameworkCore");
    }

    /// <summary>The control: lookups that are all shared register no observer at all.</summary>
    /// <remarks>
    ///     Their loaders do nothing per tenant, so an observer would be a subscription whose every
    ///     call is a no-op — the runtime branch this codebase decides at compile time instead.
    /// </remarks>
    [Fact]
    public void SharedLookupsOnly_RegisterNoObserver()
    {
        var source = Render(BuildLookups());

        source.Should().NotContain("ITenantLifecycleObserver");
    }

    [Fact]
    public void Validate_EmptyLookups_DoesNotRender()
    {
        var template = new LookupCacheRegistrationTemplate(ImmutableArray<EntityMetadataModel>.Empty);
        var artifact = template.RenderOutput();
        artifact.Text.Should().NotContain("class");
    }

    private static string Render(ImmutableArray<EntityMetadataModel> lookups)
    {
        var template = new LookupCacheRegistrationTemplate(lookups);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static ImmutableArray<EntityMetadataModel> BuildLookups() =>
        ImmutableArray.Create(BuildEntity("Sales", "Category", "Guid"));

    private static EntityMetadataModel BuildEntity(string ns, string name, string idType) => new()
    {
        TypeName = name,
        FullTypeName = $"{ns}.{name}",
        Namespace = ns,
        IdType = idType,
        IsLookup = true,
        IsValid = true
    };

    private static EntityMetadataModel TenantScoped(EntityMetadataModel entity)
        => entity with { IsTenantEntity = true };
}
