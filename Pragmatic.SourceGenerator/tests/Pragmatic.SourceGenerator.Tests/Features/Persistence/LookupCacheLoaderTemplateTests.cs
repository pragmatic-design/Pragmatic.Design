using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     What the generated loader does when a tenant appears after startup.
/// </summary>
/// <remarks>
///     Whether a lookup has one cache for the whole process or one per tenant is known here, so the
///     loader for a shared lookup does not carry a per-tenant branch that would never be taken: the
///     decision is made at generation time rather than left to a runtime <c>if</c> that answers "no"
///     in every application that has no tenants.
/// </remarks>
public class LookupCacheLoaderTemplateTests
{
    /// <summary>A tenant-scoped lookup loads for the tenant it is handed.</summary>
    [Fact]
    public void ATenantScopedLookup_LoadsForTheTenantItIsHanded()
    {
        var source = Render(tenantScoped: true);

        source.Should().Contain("Task LoadForTenantAsync(");
        source.Should().Contain("return LoadOneAsync(serviceProvider, tenantId, ct);");
    }

    /// <summary>The control: a lookup that is not tenant-scoped does nothing per tenant.</summary>
    /// <remarks>
    ///     Without it, "the loader follows the tenants" is satisfied by a loader that reloads the one
    ///     shared cache every time any tenant is created — reading the whole table again on a signal
    ///     that has nothing to do with it.
    /// </remarks>
    [Fact]
    public void ASharedLookup_DoesNothingPerTenant()
    {
        var source = Render(tenantScoped: false);

        source.Should().Contain("Task LoadForTenantAsync(");
        source.Should().Contain("return global::System.Threading.Tasks.Task.CompletedTask;");
        source.Should().NotContain("return LoadOneAsync(serviceProvider, tenantId, ct);");
    }

    /// <summary>Startup still loads every tenant the store knows, through the same body.</summary>
    /// <remarks>
    ///     The two entry points share <c>LoadOneAsync</c> on purpose: a tenant that arrives at startup
    ///     and one that arrives an hour later have to get the same cache, and two copies of the read
    ///     would be two chances for them to stop agreeing.
    /// </remarks>
    [Fact]
    public void BothEntryPoints_ReadThroughTheSameBody()
    {
        var source = Render(tenantScoped: true);

        source.Should().Contain("TenantsToLoadAsync(serviceProvider, true, \"Category\"");
        source.Should().Contain("await LoadOneAsync(serviceProvider, tenantId, ct).ConfigureAwait(false);");
        source.Should().Contain("private async global::System.Threading.Tasks.Task LoadOneAsync(");
    }

    private static string Render(bool tenantScoped)
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Category",
            FullTypeName = "Sales.Category",
            Namespace = "Sales",
            IdType = "System.Guid",
            IsLookup = true,
            IsTenantEntity = tenantScoped,
            IsValid = true
        };

        return new LookupCacheLoaderTemplate(model).RenderOutput().Text;
    }
}
