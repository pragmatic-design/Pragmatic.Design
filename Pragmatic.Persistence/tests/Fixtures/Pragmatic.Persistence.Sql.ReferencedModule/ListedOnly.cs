using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;

namespace Pragmatic.Persistence.ReferencedModule;

/// <summary>
///     A supplier taken off the list is not read: a rule of this module, which a module reading its suppliers sees as metadata only. Its
///     predicate reads a constant of the rule and a static of another type, which the source it states must carry as values and qualified names.
/// </summary>
public sealed class ListedOnly : VisibilityRule<Supplier>
{
    private const string Delisted = "DELISTED";

    public override Expression<Func<Supplier, bool>> ToExpression() => supplier => supplier.Status != Delisted && supplier.Status != SupplierStatuses.Banned;
}
