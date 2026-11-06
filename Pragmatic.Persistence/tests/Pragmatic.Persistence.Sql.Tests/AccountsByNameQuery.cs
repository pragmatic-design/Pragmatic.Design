using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;

namespace PragmaticEntities.Tests;

/// <summary>The accounts of a name, sorted by their local identity's email: an owned member, stored in the column <c>Identity_Email</c> (#564).</summary>
[Query<Account>]
public partial class AccountsByNameQuery
{
    public string? Name { get; init; }

    [Sort(MapTo = "Identity.Email")]
    public SortDirection? EmailSort { get; init; }
}
