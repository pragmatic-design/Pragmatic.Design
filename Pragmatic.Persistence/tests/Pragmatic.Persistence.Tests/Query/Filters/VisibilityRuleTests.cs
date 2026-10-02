using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query.Filters;

/// <summary>
///     What a <see cref="VisibilityRule{T}" /> is, once registered: an ordinary query filter that runs
///     after the structural ones and survives every filter mode but <c>Raw</c>.
/// </summary>
/// <remarks>
///     Four claims the design rests on, each asserted rather than assumed: the rule is picked up as a
///     filter without anything generated to wrap it; it runs after soft-delete and the scopes; a
///     filter mode does not lift it; and naming it in <c>Disable&lt;TRule&gt;()</c> does — which is why
///     the rule has to be a type in the first place.
/// </remarks>
public class VisibilityRuleTests
{
    private sealed class Item
    {
        public bool IsConfirmed { get; init; }
        public bool IsDeleted { get; init; }
    }

    private sealed class ConfirmedOnly : VisibilityRule<Item>
    {
        public override Expression<Func<Item, bool>> ToExpression() => item => item.IsConfirmed;
    }

    /// <summary>Stands in for the generated soft-delete filter, at its real priority.</summary>
    private sealed class SoftDelete : IQueryFilter<Item>
    {
        public int Priority => 100;

        public Expression<Func<Item, bool>> GetFilter() => item => !item.IsDeleted;
    }

    private static DefaultQueryFilterProvider Provider(
        IQueryFilter[] filters, IQueryFilterToggle? toggle = null)
        => new(filters, new PassthroughQueryFilterTypeRegistry(), toggle);

    [Fact]
    public void ARule_IsPickedUpAsAQueryFilter()
    {
        Provider([new ConfirmedOnly()]).HasFilters<Item>().Should().BeTrue(
            "the rule implements IQueryFilter<T> itself — nothing is generated to wrap it");
    }

    /// <summary>
    ///     Structural filters first, the domain rule after.
    /// </summary>
    /// <remarks>
    ///     Not cosmetic: soft-delete is one indexed column, and letting it narrow the set before a
    ///     domain predicate runs is the cheaper order. 300 against 100 is what the base class chooses.
    /// </remarks>
    [Fact]
    public void ARule_RunsAfterTheStructuralFilters()
    {
        var filters = Provider([new ConfirmedOnly(), new SoftDelete()]).GetFilters<Item>().ToList();

        filters.Should().HaveCount(2);
        filters[0].Should().BeOfType<SoftDelete>();
        filters[1].Should().BeOfType<ConfirmedOnly>();
    }

    /// <summary>
    ///     A filter mode does not lift it.
    /// </summary>
    /// <remarks>
    ///     The provider drops filters by marker interface — permission-based in <c>Admin</c> and above,
    ///     tenant in <c>Background</c> and above. A rule implementing neither survives both, which is
    ///     the safe default: a domain rule is not something a mode should quietly undo, and making it
    ///     bypassable is an opt-in written on the rule.
    /// </remarks>
    [Theory]
    [InlineData(FilterMode.Admin)]
    [InlineData(FilterMode.Background)]
    public void AMode_DoesNotLiftARule(FilterMode mode)
    {
        var toggle = new QueryFilterToggle();
        var provider = Provider([new ConfirmedOnly()], toggle);

        using (toggle.UseMode(mode))
        {
            provider.GetFilters<Item>().Should().ContainSingle(
                "only Raw drops everything, and a rule carries neither marker interface");
        }
    }

    /// <summary>
    ///     Naming the rule lifts it, and only it.
    /// </summary>
    /// <remarks>
    ///     This is the reason the rule is a type rather than a predicate declared inline: the name in
    ///     <c>[VisibleWhen&lt;ConfirmedOnly&gt;]</c> and the name here are the same one, and both are
    ///     checked by the compiler. The soft-delete filter staying is the control — lifting one rule
    ///     must not be a way to lift the rest.
    /// </remarks>
    [Fact]
    public void NamingTheRule_LiftsItAndNothingElse()
    {
        var toggle = new QueryFilterToggle();
        var provider = Provider([new ConfirmedOnly(), new SoftDelete()], toggle);

        using (toggle.Disable<ConfirmedOnly>())
        {
            var filters = provider.GetFilters<Item>().ToList();

            filters.Should().ContainSingle();
            filters[0].Should().BeOfType<SoftDelete>();
        }

        provider.GetFilters<Item>().Should().HaveCount(2, "the scope ended, so the rule is back");
    }

    [Fact]
    public void TheExpression_IsTheOneTheRuleDeclares()
    {
        var filter = (IQueryFilter<Item>)new ConfirmedOnly();
        var predicate = filter.GetFilter().Compile();

        predicate(new Item { IsConfirmed = true }).Should().BeTrue();
        predicate(new Item { IsConfirmed = false }).Should().BeFalse();
    }
}
