using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query;

/// <summary>
///     Restricting rows by where their node sits in a hierarchy.
/// </summary>
/// <remarks>
///     <para>
///         The helper a consumer reaches for when writing an <c>IQueryFilter&lt;T&gt;</c> over an
///         organisational tree — offices, departments, regions. Nothing in the repository called it,
///         no test ran it and no page named it, and its own doc example demonstrates a different API
///         (<c>WhereInSubtree</c>, from <c>[GenerateHierarchy]</c>) rather than either of its methods.
///     </para>
///     <para>
///         Asserted here is the boundary that its name claims and that a reader would otherwise have
///         to take on trust: <c>WhereInDirectChildren</c> descends exactly one level. A filter that
///         quietly returned the whole subtree would widen every caller's data access.
///     </para>
/// </remarks>
public class HierarchyFilterTests
{
    private sealed record Office(Guid Id, Guid? ParentId);

    private sealed record Invoice(Guid Id, Guid OfficeId);

    private static readonly Guid Root = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Child = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Grandchild = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Elsewhere = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static IQueryable<Office> Offices() => new[]
    {
        new Office(Root, null),
        new Office(Child, Root),
        new Office(Grandchild, Child),
        new Office(Elsewhere, null),
    }.AsQueryable();

    private static IQueryable<Invoice> Invoices() => new[]
    {
        new Invoice(Guid.NewGuid(), Root),
        new Invoice(Guid.NewGuid(), Child),
        new Invoice(Guid.NewGuid(), Grandchild),
        new Invoice(Guid.NewGuid(), Elsewhere),
    }.AsQueryable();

    [Fact]
    public void WhereInDirectChildren_KeepsTheRootAndOneLevelBelow()
    {
        // Inferred, which TKey : struct is what allows: under notnull the compiler reads TKey? on a
        // value type as plain TKey, so the parent selector would demand an id no root can have.
        var result = HierarchyFilter.WhereInDirectChildren(
            Invoices(), Offices(), o => o.Id, o => o.ParentId, i => i.OfficeId, Root).ToList();

        result.Select(i => i.OfficeId).Should().Contain(Root);
        result.Select(i => i.OfficeId).Should().Contain(Child);
    }

    /// <remarks>
    ///     The half that matters. "Direct children" is a promise about how far the filter reaches, and
    ///     a caller relying on it to scope data would be handing out a whole subtree if it were wrong.
    /// </remarks>
    [Fact]
    public void WhereInDirectChildren_DoesNotDescendFurther()
    {
        // Inferred, which TKey : struct is what allows: under notnull the compiler reads TKey? on a
        // value type as plain TKey, so the parent selector would demand an id no root can have.
        var result = HierarchyFilter.WhereInDirectChildren(
            Invoices(), Offices(), o => o.Id, o => o.ParentId, i => i.OfficeId, Root).ToList();

        result.Select(i => i.OfficeId).Should().NotContain(Grandchild,
            "one level of descent is what the name promises");
        result.Select(i => i.OfficeId).Should().NotContain(Elsewhere);
    }

    /// <remarks>
    ///     The other method takes the node set already resolved — by a recursive CTE, or by anything
    ///     else — and only restricts rows to it. It is the one to combine with deep traversal.
    /// </remarks>
    [Fact]
    public void WhereNodeIn_KeepsOnlyRowsOnTheNodesGiven()
    {
        var allowed = new[] { Root, Grandchild }.AsQueryable();

        var result = HierarchyFilter.WhereNodeIn(Invoices(), i => i.OfficeId, allowed).ToList();

        result.Select(i => i.OfficeId).Should().BeEquivalentTo(new[] { Root, Grandchild });
    }
}
