using System.Collections;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Query;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A <c>[Sort]</c> with a <c>DefaultDirection</c> orders after the sorts the caller chose, whatever the declaration
///     order (#642).
/// </summary>
/// <remarks>
///     The generated <c>Apply</c> applied the sorts in declaration order, so a default declared before the caller's
///     sort ordered first and the caller's choice only broke its ties. The query below declares the default on
///     <c>Created</c> first, as an application did; the generated <c>Apply</c> is run over rows.
/// </remarks>
public class ADefaultSortComesAfterTheCallersTests
{
    private const string Source = """
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class Order
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public int Created { get; set; }
        }

        [MapFrom<Order>]
        [GenerateProjection]
        public partial class OrderDto
        {
            public string Code { get; set; } = "";
        }

        [Query<Order, OrderDto>]
        public partial class ListOrdersQuery
        {
            [Sort(DefaultDirection = SortDirection.Descending)]
            public SortDirection? CreatedSort { get; init; }

            [Sort]
            public SortDirection? NameSort { get; init; }
        }
        """;

    // Ordered by Created descending these read A, B, C; by Name, then Created descending, B, C, A.
    private static readonly (string Code, string Name, int Created)[] Rows = [("A", "b", 3), ("B", "a", 2), ("C", "a", 1)];

    private static readonly Lazy<Assembly> Compiled = new(Compile);

    [Fact]
    public void TheCallersSort_OrdersFirst_AndTheDefaultBreaksItsTies()
        => Codes(nameSort: SortDirection.Ascending, createdSort: null).Should().Equal("B", "C", "A");

    /// <summary>The control: with no sort from the caller, the default alone orders.</summary>
    [Fact]
    public void WithNoSortFromTheCaller_TheDefaultOrders()
        => Codes(nameSort: null, createdSort: null).Should().Equal("A", "B", "C");

    /// <summary>
    ///     A direction given for the default sort is honoured, still after the caller's other sort: by <c>Created</c>
    ///     first these would read C, B, A.
    /// </summary>
    [Fact]
    public void ADirectionGivenForTheDefaultSort_IsHonouredInItsPlace()
        => Codes(nameSort: SortDirection.Descending, createdSort: SortDirection.Ascending).Should().Equal("A", "C", "B");

    /// <remarks>
    ///     The reflection is the test harness's, not the framework's: the types being exercised exist only inside the
    ///     compilation the generator just produced.
    /// </remarks>
    private static List<string> Codes(SortDirection? nameSort, SortDirection? createdSort)
    {
        var assembly = Compiled.Value;
        var order = assembly.GetType("TestApp.Order")!;
        var queryType = assembly.GetType("TestApp.ListOrdersQuery")!;

        var query = Activator.CreateInstance(queryType)!;
        queryType.GetProperty("NameSort")!.SetValue(query, nameSort);
        queryType.GetProperty("CreatedSort")!.SetValue(query, createdSort);

        var rows = Array.CreateInstance(order, Rows.Length);
        for (var i = 0; i < Rows.Length; i++)
        {
            var row = Activator.CreateInstance(order)!;
            order.GetProperty("Code")!.SetValue(row, Rows[i].Code);
            order.GetProperty("Name")!.SetValue(row, Rows[i].Name);
            order.GetProperty("Created")!.SetValue(row, Rows[i].Created);
            rows.SetValue(row, i);
        }

        var queryable = typeof(Queryable).GetMethods()
            .First(m => m.Name == nameof(Queryable.AsQueryable) && m.IsGenericMethod)
            .MakeGenericMethod(order)
            .Invoke(null, [rows])!;
        var applied = (IEnumerable)queryType.GetMethod("Apply")!.Invoke(query, [queryable])!;

        return [.. applied.Cast<object>().Select(o => (string)order.GetProperty("Code")!.GetValue(o)!)];
    }

    private static Assembly Compile()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source,
        [
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
            GeneratorTestHelper.FromType<Result.IError>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(Result.Result<,>)),
            GeneratorTestHelper.FromType<Actions.Invoker.QueryInvoker<object>>(),
            GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
            GeneratorTestHelper.FromType<Specification.Specification<object>>(),
            GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.ComponentModel.IListSource)),
            GeneratorTestHelper.FromTypeAssembly(typeof(Queryable)),
            GeneratorTestHelper.FromTypeAssembly(typeof(System.Linq.Expressions.Expression)),
        ]);

        using var stream = new MemoryStream();
        var emit = result.OutputCompilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("; ", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())));

        return Assembly.Load(stream.ToArray());
    }
}
