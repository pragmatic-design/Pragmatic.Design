using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Scopes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Scopes;

/// <summary>
///     Registering a data scope rule, and the filter that has to come with it.
/// </summary>
/// <remarks>
///     <para>
///         <c>AddDataScopeRule</c> is the entry point for level-3 data access.
///     </para>
///     <para>
///         The filter must not go in with <c>TryAdd</c>, which asks whether <em>any</em>
///         <c>IQueryFilter</c> is registered. Soft-delete or tenant always gets there first, so the
///         line would skip every time and the computed rule would silently never filter. Without a
///         test, nothing would notice the guarantee stopping.
///     </para>
/// </remarks>
public class DataScopeServiceExtensionsTests
{
    private sealed class Invoice : IScopedEntity
    {
        public List<string> AccessScopes { get; } = [];
    }

    private sealed class OfficeScopeRule : DataScopeRule<Invoice>
    {
        public override string ScopeName => "office";

        public override Expression<Func<Invoice, bool>> ToExpression() => _ => true;
    }

    /// <remarks>
    ///     A filter that is already there, standing in for soft-delete or tenant — the ones that in a
    ///     real application are registered long before any scope rule.
    /// </remarks>
    private sealed class SomeOtherFilter : IQueryFilter;

    [Fact]
    public void AddDataScopeRule_RegistersTheRuleUnderBothItsShapes()
    {
        var services = new ServiceCollection();

        services.AddDataScopeRule<OfficeScopeRule, Invoice>();

        services.Should().Contain(
            d => d.ServiceType == typeof(DataScopeRule<Invoice>)
                 && d.ImplementationType == typeof(OfficeScopeRule));
        services.Should().Contain(d => d.ServiceType == typeof(DataScopeRule),
            "the untyped shape is how the rule is found without knowing the entity");
    }

    /// <summary>
    ///     The filter goes in even when another one is already registered.
    /// </summary>
    /// <remarks>
    ///     This is the regression the comment describes, and the failure it produced was silence: the
    ///     rule was registered, the application looked configured, and rows the scope should have
    ///     restricted came back in full.
    /// </remarks>
    [Fact]
    public void AddDataScopeRule_AddsItsFilterEvenBehindAnExistingOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IQueryFilter, SomeOtherFilter>();

        services.AddDataScopeRule<OfficeScopeRule, Invoice>();

        services.Should().Contain(
            d => d.ServiceType == typeof(IQueryFilter)
                 && d.ImplementationType == typeof(ComputedScopeFilter<Invoice>),
            "TryAdd would have seen an IQueryFilter already there and skipped, leaving the rule inert");
    }
}
