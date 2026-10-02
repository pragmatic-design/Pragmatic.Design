using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The registration this template emits for <c>IQueryExecutor</c>.
/// </summary>
/// <remarks>
///     Every argument the executor needs is passed here as an optional parameter, so a template that
///     stopped passing one would still compile — in the generated code and in every consumer — and the
///     capability behind it would simply stop happening. <c>ICacheStackResolver</c>, for one, could be
///     added to the constructor and never reach it: category routing would be wired everywhere except
///     the one place that builds the object.
/// </remarks>
public class DbContextRegistrationTemplateTests
{
    [Fact]
    public void QueryExecutor_IsRegistered()
    {
        Render().Should().Contain(
            "services.AddScoped<global::Pragmatic.Persistence.Query.Executors.IQueryExecutor>(sp =>");
    }

    [Theory]
    // Each of these carries a capability that fails silently — no exception, no log — when the
    // argument stops being passed, because every one of them is optional on the constructor.
    [InlineData("global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider", "global query filters (soft-delete, tenant)")]
    [InlineData("global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer", "filter composition")]
    [InlineData("global::Pragmatic.Caching.ICacheStack", "the default cache stack")]
    [InlineData("global::Pragmatic.MultiTenancy.ITenantContext", "the tenant discriminator in the cache key")]
    [InlineData("global::Pragmatic.Identity.ICurrentUser", "the user discriminator in the cache key")]
    [InlineData("global::Pragmatic.Caching.ICacheStackResolver", "routing a [Cacheable(Category = ...)] query to that category's stack")]
    public void QueryExecutor_ReceivesEveryDependencyItNeeds(string service, string capability)
    {
        Render().Should().Contain($"sp.GetService<{service}>()",
            $"without it the executor silently loses {capability}");
    }

    /// <summary>
    ///     The tenant interceptor is built with the application's own <c>RequireTenant</c>, which is the
    ///     only way that setting can reach it.
    /// </summary>
    /// <remarks>
    ///     The interceptor cannot name <c>MultiTenancyOptions</c> — that package references
    ///     <c>Pragmatic.Persistence.EFCore</c>, so the reverse would be a cycle — and its parameter
    ///     therefore defaults to <see langword="false" />, which keeps an interceptor built by hand
    ///     behaving as it always did. That default is also what a registration that stopped passing the
    ///     argument would silently fall back to: the write path would go back to not refusing, with the
    ///     application still reading <c>RequireTenant = true</c> and the read filter still honouring it.
    /// </remarks>
    [Fact]
    public void TenantInterceptor_IsBuiltWithTheApplicationsRequireTenant()
    {
        var registration = Render(hasMultiTenancy: true);

        registration.Should().Contain(
            "requireTenant: sp.GetRequiredService<global::Microsoft.Extensions.Options.IOptions<"
            + "global::Pragmatic.MultiTenancy.MultiTenancyOptions>>().Value.RequireTenant)",
            "or the refusal is off in every host, whatever the application configured");
    }

    private static string Render(bool hasMultiTenancy = false)
    {
        var template = new DbContextRegistrationTemplate(
            [
                new BoundaryDbContextModel
                {
                    Namespace = "Sales",
                    ClassName = "SalesDbContext",
                    BoundaryName = "Sales",
                }
            ],
            namespacePrefix: "Sales",
            hasMultiTenancy: hasMultiTenancy);

        return template.RenderOutput().Text;
    }
}
