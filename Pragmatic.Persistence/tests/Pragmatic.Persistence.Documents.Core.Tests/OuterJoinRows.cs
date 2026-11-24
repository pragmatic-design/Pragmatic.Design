using Pragmatic.Persistence.Documents.Workload;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The rows an outer join of the early orders (<c>ORD-1000…</c>) with the customers of one country gives, computed from the seed in memory:
///     the pairs whose keys match, the orders without such a customer, the customers without such an order. Each row is <c>number|name</c>,
///     <c>-</c> for the side that is missing, sorted, so two engines' rows compare whatever order they come in.
/// </summary>
internal sealed class OuterJoinRows
{
    public const string Prefix = "ORD-1000";

    public OuterJoinRows(IEnumerable<OrderDocument> orders, IEnumerable<CustomerDocument> customers, string country)
    {
        var outer = orders.Where(o => o.Number.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
        var inner = customers.Where(c => c.Country == country).ToList();
        Pairs = [.. from o in outer join c in inner on o.CustomerId equals c.Id select Row(o.Number, c.Name)];
        OrdersAlone = [.. outer.Where(o => inner.All(c => c.Id != o.CustomerId)).Select(o => Row(o.Number, null))];
        CustomersAlone = [.. inner.Where(c => outer.All(o => o.CustomerId != c.Id)).Select(c => Row(null, c.Name))];
    }

    public List<string> Pairs { get; }

    public List<string> OrdersAlone { get; }

    public List<string> CustomersAlone { get; }

    /// <summary>True when each kind of row is there: a join compared on data without unmatched rows proves nothing about them.</summary>
    public bool HasEveryKind => Pairs.Count > 0 && OrdersAlone.Count > 0 && CustomersAlone.Count > 0;

    public List<string> Left => Sorted(Pairs.Concat(OrdersAlone));

    public List<string> Right => Sorted(Pairs.Concat(CustomersAlone));

    public List<string> Full => Sorted(Pairs.Concat(OrdersAlone).Concat(CustomersAlone));

    public static string Row(string? number, string? name) => (number ?? "-") + "|" + (name ?? "-");

    public static List<string> Sorted(IEnumerable<string> rows) => [.. rows.Order(StringComparer.Ordinal)];
}
