namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     Publishes a <c>[Query]</c> into its boundary's read contract: the generator emits an
///     <c>I{Boundary}Reads</c> interface (plus a DI-registered implementation that runs the query) so that
///     <b>another</b> boundary can enforce a cross-boundary invariant by injecting the read contract — never
///     the owning module — keeping the dependency acyclic (#3). A validator that must check, say, a plan limit
///     owned by the Subscription module injects <c>ISubscriptionReads</c> instead of reaching into it.
/// </summary>
/// <remarks>
///     Apply to a class already annotated with <c>[Query&lt;TEntity, TDto&gt;]</c>. The generated interface
///     lives in a <c>.Contracts</c> sub-namespace of the boundary so consumers reference only the contract.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PublishedAttribute : Attribute
{
    /// <summary>
    ///     Optional explicit read-contract interface name. When unset, defaults to <c>I{Boundary}Reads</c>
    ///     derived from the query's boundary.
    /// </summary>
    public string? ContractName { get; init; }

    /// <summary>
    ///     Optional explicit name for the method this query contributes to the contract. When unset,
    ///     defaults to the class name with a trailing <c>Query</c> removed — <c>SearchCategoriesQuery</c>
    ///     contributes <c>SearchCategories</c>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The suffix is removed because the class name whole would make a consumer write
    ///     <c>SearchCategoriesQuery(new SearchCategoriesQuery { … })</c> — the type name twice on one
    ///     line, on the only surface this feature has. Set this when the class name is not the name the
    ///     contract should read by, or when two published queries would otherwise contribute the same
    ///     one (<c>PRAG0728</c>).
    /// </remarks>
    public string? MethodName { get; init; }
}
