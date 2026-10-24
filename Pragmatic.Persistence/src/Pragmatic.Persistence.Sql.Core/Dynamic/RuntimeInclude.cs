using System.Data.Common;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>A node of a runtime chain's include tree: a to-one navigation (a join, an object set on its owner) or a collection (a second query on the parents' keys).</summary>
internal sealed class RuntimeInclude
{
    public RuntimeInclude(string property, SqlEntityMap target, SqlNavigation? navigation, SqlCollection? collection, LambdaExpression? filter = null, List<(LambdaExpression Key, bool Descending)>? orderings = null, int? take = null)
    {
        Property = property;
        Target = target;
        Navigation = navigation;
        Collection = collection;
        Filter = filter;
        Orderings = orderings ?? [];
        Take = take;
    }

    /// <summary>The children's ordering (<c>c.Orders.OrderBy(o => o.CreatedAt)</c>, <c>ThenBy</c>): columns only; the companion statement orders them.</summary>
    public List<(LambdaExpression Key, bool Descending)> Orderings { get; }

    /// <summary>At most this many children per parent (<c>.OrderBy(…).Take(3)</c>: a window partitioned by the foreign key); null for all.</summary>
    public int? Take { get; }

    public string Property { get; }

    public SqlEntityMap Target { get; }

    public SqlNavigation? Navigation { get; }

    public SqlCollection? Collection { get; }

    /// <summary>The collection's filter (<c>c.Orders.Where(o => !o.IsDeleted)</c>): columns and constants, added to the child query's WHERE.</summary>
    public LambdaExpression? Filter { get; }

    public List<RuntimeInclude> Children { get; } = [];

    public bool IsCollection => Collection is not null;

    public bool LoadsAnything => IsCollection || Children.Any(c => c.LoadsAnything);
}
