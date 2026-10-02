using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Query.Visitors;
using Pragmatic.Persistence.Query.Filters;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

public class PragmaticQueryFilterVisitorTests
{
    [Fact]
    public void Visit_EmptyFilterMap_NoChanges()
    {
        var visitor = new PragmaticQueryFilterVisitor(FilterMap.Empty);
        Expression<Func<Order, IEnumerable<LineItem>>> expr = o => o.Items;

        var result = visitor.Apply(expr);

        result.Should().BeSameAs(expr);
    }

    [Fact]
    public void VisitMember_CollectionWithSoftDelete_InjectsWhere()
    {
        var filterMap = CreateFilterMap<LineItem>(e => !e.IsDeleted);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, IEnumerable<LineItem>>> expr = o => o.Items;
        var result = visitor.Apply(expr);

        var resultStr = result.ToString();
        resultStr.Should().Contain("Where");
        resultStr.Should().Contain("IsDeleted");
    }

    [Fact]
    public void VisitMember_CollectionWithoutFilter_PassesThrough()
    {
        // Filter exists for LineItem but NOT for Payment
        var filterMap = CreateFilterMap<LineItem>(e => !e.IsDeleted);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, IEnumerable<Payment>>> expr = o => o.Payments;
        var result = visitor.Apply(expr);

        var resultStr = result.ToString();
        resultStr.Should().NotContain("Where");
    }

    [Fact]
    public void VisitMember_NonCollectionProperty_PassesThrough()
    {
        var filterMap = CreateFilterMap<LineItem>(e => !e.IsDeleted);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, string>> expr = o => o.Description;
        var result = visitor.Apply(expr);

        var resultStr = result.ToString();
        resultStr.Should().NotContain("Where");
    }

    [Fact]
    public void VisitMember_StringProperty_NotTreatedAsCollection()
    {
        // Even with filters, string properties should not be treated as collections
        var filterMap = CreateFilterMap<LineItem>(e => !e.IsDeleted);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, string>> expr = o => o.Description;
        var result = visitor.Apply(expr);

        var resultStr = result.ToString();
        resultStr.Should().NotContain("Where");
    }

    [Fact]
    public void Visit_NestedNavigation_FiltersAtEachLevel()
    {
        var filters = new Dictionary<Type, LambdaExpression>
        {
            [typeof(LineItem)] = (Expression<Func<LineItem, bool>>)(e => !e.IsDeleted),
            [typeof(LineItemDetail)] = (Expression<Func<LineItemDetail, bool>>)(d => d.IsActive)
        };
        var filterMap = new FilterMap(filters);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        // o => o.Items (collection of LineItem, each has Details collection)
        Expression<Func<Order, IEnumerable<LineItem>>> expr = o => o.Items;
        var result = visitor.Apply(expr);

        var resultStr = result.ToString();
        resultStr.Should().Contain("Where");
        resultStr.Should().Contain("IsDeleted");
    }

    [Fact]
    public void Visit_MultipleFilterTypes_AppliesCorrectFilter()
    {
        var filters = new Dictionary<Type, LambdaExpression>
        {
            [typeof(LineItem)] = (Expression<Func<LineItem, bool>>)(e => !e.IsDeleted),
            [typeof(Payment)] = (Expression<Func<Payment, bool>>)(p => p.Amount > 0)
        };
        var filterMap = new FilterMap(filters);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, IEnumerable<LineItem>>> itemsExpr = o => o.Items;
        Expression<Func<Order, IEnumerable<Payment>>> paymentsExpr = o => o.Payments;

        var itemsResult = visitor.Apply(itemsExpr).ToString();
        var paymentsResult = visitor.Apply(paymentsExpr).ToString();

        itemsResult.Should().Contain("IsDeleted");
        paymentsResult.Should().Contain("Amount");
    }

    [Fact]
    public void Apply_FilterMapWithFilters_ReturnsModifiedExpression()
    {
        var filterMap = CreateFilterMap<LineItem>(e => !e.IsDeleted);
        var visitor = new PragmaticQueryFilterVisitor(filterMap);

        Expression<Func<Order, IEnumerable<LineItem>>> expr = o => o.Items;
        var result = visitor.Apply(expr);

        result.Should().NotBeSameAs(expr);
    }

    private static FilterMap CreateFilterMap<T>(Expression<Func<T, bool>> filter) where T : class
    {
        return new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(T)] = filter
        });
    }

    // Test entity stubs
    private class Order
    {
        public string Description { get; set; } = "";
        public ICollection<LineItem> Items { get; set; } = new List<LineItem>();
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }

    private class LineItem
    {
        public bool IsDeleted { get; set; }
        public ICollection<LineItemDetail> Details { get; set; } = new List<LineItemDetail>();
    }

    private class LineItemDetail
    {
        public bool IsActive { get; set; }
    }

    private class Payment
    {
        public decimal Amount { get; set; }
    }
}
