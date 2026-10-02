using System.Linq.Expressions;
using System.Reflection;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.Tests.Query.Filters;

public class FilterMapTests
{
    [Fact]
    public void Empty_HasNoFilters()
    {
        FilterMap.Empty.HasFilters.Should().BeFalse();
        FilterMap.Empty.Count.Should().Be(0);
    }

    [Fact]
    public void TryGetFilter_ExistingType_ReturnsTrue()
    {
        var filter = CreateSoftDeleteFilter<Invoice>();
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = filter
        });

        map.TryGetFilter(typeof(Invoice), out var result).Should().BeTrue();
        result.Should().BeSameAs(filter);
    }

    [Fact]
    public void TryGetFilter_NonExistingType_ReturnsFalse()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        map.TryGetFilter(typeof(Guest), out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryGetFilter_Generic_ReturnsCastExpression()
    {
        Expression<Func<Invoice, bool>> filter = e => !e.IsDeleted;
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = filter
        });

        map.TryGetFilter<Invoice>(out var result).Should().BeTrue();
        result.Should().NotBeNull();
        result.Should().BeAssignableTo<Expression<Func<Invoice, bool>>>();
    }

    [Fact]
    public void HasFilterFor_ExistingType_ReturnsTrue()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        map.HasFilterFor<Invoice>().Should().BeTrue();
        map.HasFilterFor<Guest>().Should().BeFalse();
    }

    [Fact]
    public void FilteredTypes_ReturnsAllTypes()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>(),
            [typeof(LineItem)] = CreateSoftDeleteFilter<LineItem>()
        });

        map.FilteredTypes.Should().BeEquivalentTo(new[] { typeof(Invoice), typeof(LineItem) });
    }

    [Fact]
    public void Merge_EmptyWithFilters_ReturnsFilters()
    {
        var additional = new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        };

        var result = FilterMap.Empty.Merge(additional);

        result.HasFilterFor<Invoice>().Should().BeTrue();
        result.Count.Should().Be(1);
    }

    [Fact]
    public void Merge_FiltersWithEmpty_ReturnsSameMap()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var result = map.Merge(new Dictionary<Type, LambdaExpression>());

        result.Should().BeSameAs(map);
    }

    [Fact]
    public void Merge_DisjointTypes_CombinesBoth()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var additional = new Dictionary<Type, LambdaExpression>
        {
            [typeof(LineItem)] = CreateSoftDeleteFilter<LineItem>()
        };

        var result = map.Merge(additional);

        result.Count.Should().Be(2);
        result.HasFilterFor<Invoice>().Should().BeTrue();
        result.HasFilterFor<LineItem>().Should().BeTrue();
    }

    [Fact]
    public void Merge_SameType_CombinesWithAnd()
    {
        Expression<Func<Invoice, bool>> softDelete = e => !e.IsDeleted;
        Expression<Func<Invoice, bool>> tenant = e => e.TenantId == "t1";

        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = softDelete
        });

        var additional = new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = tenant
        };

        var result = map.Merge(additional);

        result.Count.Should().Be(1);
        result.TryGetFilter<Invoice>(out var combined).Should().BeTrue();

        // The combined expression should be an AndAlso
        combined!.Body.NodeType.Should().Be(ExpressionType.AndAlso);
    }

    [Fact]
    public void Merge_TwoFilterMaps_Works()
    {
        var map1 = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var map2 = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(LineItem)] = CreateSoftDeleteFilter<LineItem>()
        });

        var result = map1.Merge(map2);
        result.Count.Should().Be(2);
    }

    [Fact]
    public void MergeFilterMap_OtherEmpty_ReturnsThis()
    {
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var result = map.Merge(FilterMap.Empty);

        result.Should().BeSameAs(map);
    }

    [Fact]
    public void MergeFilterMap_ThisEmptyWithoutWhereMethods_ReturnsOther()
    {
        var other = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var result = FilterMap.Empty.Merge(other);

        result.Should().BeSameAs(other);
    }

    [Fact]
    public void MergeFilterMap_ThisHasOnlyWhereMethods_PreservesThemWhenAdoptingOtherFilters()
    {
        // Regression: when this map has no filters but DOES carry WhereMethods, merging with a
        // map that has filters must not silently discard this map's WhereMethods.
        var whereMethod = GetEnumerableWhereMethod<Invoice>();
        var thisMap = new FilterMap(
            new Dictionary<Type, LambdaExpression>(),
            new Dictionary<Type, MethodInfo> { [typeof(Invoice)] = whereMethod });

        var other = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>()
        });

        var result = thisMap.Merge(other);

        result.HasFilterFor<Invoice>().Should().BeTrue();
        result.TryGetWhereMethod(typeof(Invoice), out var method).Should().BeTrue();
        method.Should().BeSameAs(whereMethod);
    }

    [Fact]
    public void MergeFilterMap_BothHaveWhereMethods_KeepsThisMethodOnConflict()
    {
        // "ours take precedence" per the Merge(FilterMap) contract (TryAdd keeps the existing).
        var thisMethod = GetEnumerableWhereMethod<Invoice>();
        var otherMethod = GetEnumerableWhereMethod<Invoice>();

        var thisMap = new FilterMap(
            new Dictionary<Type, LambdaExpression> { [typeof(Invoice)] = CreateSoftDeleteFilter<Invoice>() },
            new Dictionary<Type, MethodInfo> { [typeof(Invoice)] = thisMethod });

        var otherMap = new FilterMap(
            new Dictionary<Type, LambdaExpression> { [typeof(LineItem)] = CreateSoftDeleteFilter<LineItem>() },
            new Dictionary<Type, MethodInfo> { [typeof(Invoice)] = otherMethod });

        var result = thisMap.Merge(otherMap);

        result.Count.Should().Be(2);
        result.TryGetWhereMethod(typeof(Invoice), out var method).Should().BeTrue();
        method.Should().BeSameAs(thisMethod);
    }

    [Fact]
    public void MergeFilterMap_SameType_CombinesFiltersWithAnd()
    {
        Expression<Func<Invoice, bool>> softDelete = e => !e.IsDeleted;
        Expression<Func<Invoice, bool>> tenant = e => e.TenantId == "t1";

        var map1 = new FilterMap(new Dictionary<Type, LambdaExpression> { [typeof(Invoice)] = softDelete });
        var map2 = new FilterMap(new Dictionary<Type, LambdaExpression> { [typeof(Invoice)] = tenant });

        var result = map1.Merge(map2);

        result.Count.Should().Be(1);
        result.TryGetFilter<Invoice>(out var combined).Should().BeTrue();
        combined!.Body.NodeType.Should().Be(ExpressionType.AndAlso);
    }

    [Fact]
    public void TryGetFilterGeneric_WrongStoredType_ReturnsFalse()
    {
        // The generic accessor must not surface a filter stored under the same key but with an
        // incompatible lambda type (it does a typed `as` cast, not an unchecked cast).
        Expression<Func<LineItem, bool>> wrongTyped = e => !e.IsDeleted;
        var map = new FilterMap(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = wrongTyped
        });

        map.TryGetFilter<Invoice>(out var result).Should().BeFalse();
        result.Should().BeNull();
    }

    private static MethodInfo GetEnumerableWhereMethod<T>()
        => typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.Where)
                        && m.GetParameters() is { Length: 2 } p
                        && p[1].ParameterType.GetGenericArguments().Length == 2)
            .MakeGenericMethod(typeof(T));

    [Fact]
    public void CombineWithAnd_ProducesValidExpression()
    {
        Expression<Func<Invoice, bool>> left = e => !e.IsDeleted;
        Expression<Func<Invoice, bool>> right = e => e.TenantId == "t1";

        var combined = (Expression<Func<Invoice, bool>>)FilterMap.CombineWithAnd(left, right);

        // Compile and test
        var func = combined.Compile();
        func(new Invoice { IsDeleted = false, TenantId = "t1" }).Should().BeTrue();
        func(new Invoice { IsDeleted = true, TenantId = "t1" }).Should().BeFalse();
        func(new Invoice { IsDeleted = false, TenantId = "t2" }).Should().BeFalse();
        func(new Invoice { IsDeleted = true, TenantId = "t2" }).Should().BeFalse();
    }

    // Helper method
    private static Expression<Func<T, bool>> CreateSoftDeleteFilter<T>() where T : ISoftDeletable
        => e => !e.IsDeleted;

    // Test entity stubs
    private interface ISoftDeletable
    {
        bool IsDeleted { get; }
    }

    private sealed class Invoice : ISoftDeletable
    {
        public bool IsDeleted { get; set; }
        public string TenantId { get; set; } = "";
    }

    private sealed class LineItem : ISoftDeletable
    {
        public bool IsDeleted { get; set; }
    }

    private sealed class Guest
    {
        public string Name { get; set; } = "";
    }
}
