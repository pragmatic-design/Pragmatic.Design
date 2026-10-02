using System.Collections.Generic;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.SourceGenerator.Synthesis;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     Verifies the #7 phase-3 FK topological ordering: parents are created before the children that reference
///     them; cycles are handled without hanging.
/// </summary>
public class FkTopologicalSorterTests
{
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Graph(
        params (string Entity, string[] DependsOn)[] edges)
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (entity, dependsOn) in edges)
            graph[entity] = dependsOn;
        return graph;
    }

    [Fact]
    public void Order_PlacesParentsBeforeChildren()
    {
        // Line → Invoice → Customer (each references the next as a required FK).
        var graph = Graph(
            ("Line", ["Invoice"]),
            ("Invoice", ["Customer"]),
            ("Customer", []));

        var order = new System.Collections.Generic.List<string>(FkTopologicalSorter.Order(graph));

        order.Should().HaveCount(3);
        order.IndexOf("Customer").Should().BeLessThan(order.IndexOf("Invoice"));
        order.IndexOf("Invoice").Should().BeLessThan(order.IndexOf("Line"));
    }

    [Fact]
    public void Order_DiamondDependencies_RespectsAllEdges()
    {
        // Order → {Customer, Address}; both → Tenant.
        var graph = Graph(
            ("Order", ["Customer", "Address"]),
            ("Customer", ["Tenant"]),
            ("Address", ["Tenant"]),
            ("Tenant", []));

        var order = new System.Collections.Generic.List<string>(FkTopologicalSorter.Order(graph));

        order.IndexOf("Tenant").Should().BeLessThan(order.IndexOf("Customer"));
        order.IndexOf("Tenant").Should().BeLessThan(order.IndexOf("Address"));
        order.IndexOf("Customer").Should().BeLessThan(order.IndexOf("Order"));
        order.IndexOf("Address").Should().BeLessThan(order.IndexOf("Order"));
    }

    [Fact]
    public void Order_WithCycle_DoesNotHang_AndIncludesAll()
    {
        var graph = Graph(
            ("A", ["B"]),
            ("B", ["A"]));

        var order = new System.Collections.Generic.List<string>(FkTopologicalSorter.Order(graph));

        order.Should().BeEquivalentTo(["A", "B"]);
    }

    [Fact]
    public void HasCycle_DetectsCircularFk()
    {
        FkTopologicalSorter.HasCycle(Graph(("A", ["B"]), ("B", ["A"]))).Should().BeTrue();
        FkTopologicalSorter.HasCycle(Graph(("A", ["B"]), ("B", []))).Should().BeFalse();
    }
}
