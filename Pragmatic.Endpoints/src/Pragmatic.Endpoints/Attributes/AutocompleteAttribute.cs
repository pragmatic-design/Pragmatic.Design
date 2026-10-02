namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Marks an entity property for autocomplete endpoint generation.
///     Generates a GET endpoint that searches this property and returns
///     <see cref="Pragmatic.Endpoints.Responses.AutocompleteItem{TKey}"/> with typed Id.
/// </summary>
/// <example>
///     <code>
/// public class Customer
/// {
///     public Guid Id { get; set; }
///     [Autocomplete] public string Name { get; set; } = "";
///     [Autocomplete(Route = "api/customers/search-by-code")] public string Code { get; set; } = "";
/// }
/// </code>
/// </example>
/// <remarks>
///     <para>
///         <b>The endpoint offers every row the query filters leave.</b> Soft-delete and tenant apply,
///         and so does any <c>IQueryFilter&lt;TEntity&gt;</c> registered for the entity; nothing else
///         does. This attribute cannot express "only these rows", and that is deliberate — which rows
///         exist is a property of the entity, not of one endpoint.
///     </para>
///     <para>
///         So think before adding it to a domain where suggesting a row means something. If the
///         application already has a suggestion path that applies a rule — only confirmed vocabulary,
///         only active customers — this publishes a second one in a single word that does not apply
///         it, and a suggestion is accepted <em>because it was offered</em>. When the set to offer is
///         not the visible set, write a query: it can filter, page, project and be authorized on its
///         own terms. When the rule is genuinely about visibility rather than about suggesting,
///         register an <c>IQueryFilter&lt;TEntity&gt;</c> and every read gets it, this one included.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class AutocompleteAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the route template for this autocomplete endpoint.
    ///     Default: <c>api/{entity}/autocomplete/{property}</c> (all lowercase).
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    ///     Gets or sets the default result limit.
    ///     Default: 10.
    /// </summary>
    public int DefaultLimit { get; set; } = 10;
}

/// <summary>
///     Marks an entity property for autocomplete endpoint generation with a custom DTO projection.
///     The DTO must have a <c>[MapFrom&lt;TEntity&gt;]</c> attribute so that
///     the generated <c>FromEntity</c> method is available for mapping.
///     The search still filters on this string property, but returns the full DTO.
/// </summary>
/// <typeparam name="TDto">
///     The DTO type to project results into. Must have <c>[MapFrom&lt;TEntity&gt;]</c>.
/// </typeparam>
/// <example>
///     <code>
/// [MapFrom&lt;Property&gt;]
/// public partial record PropertySearchResult
/// {
///     public Guid Id { get; init; }
///     public string Name { get; init; }
///     public string City { get; init; }
///     public int StarRating { get; init; }
/// }
///
/// public class Property
/// {
///     public Guid Id { get; set; }
///     [Autocomplete&lt;PropertySearchResult&gt;] public string Name { get; set; } = "";
/// }
/// </code>
/// </example>
/// <remarks>
///     <para>
///         <b>The endpoint offers every row the query filters leave.</b> Soft-delete and tenant apply,
///         and so does any <c>IQueryFilter&lt;TEntity&gt;</c> registered for the entity; nothing else
///         does. This attribute cannot express "only these rows", and that is deliberate — which rows
///         exist is a property of the entity, not of one endpoint.
///     </para>
///     <para>
///         So think before adding it to a domain where suggesting a row means something. If the
///         application already has a suggestion path that applies a rule — only confirmed vocabulary,
///         only active customers — this publishes a second one in a single word that does not apply
///         it, and a suggestion is accepted <em>because it was offered</em>. When the set to offer is
///         not the visible set, write a query: it can filter, page, project and be authorized on its
///         own terms. When the rule is genuinely about visibility rather than about suggesting,
///         register an <c>IQueryFilter&lt;TEntity&gt;</c> and every read gets it, this one included.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class AutocompleteAttribute<TDto> : Attribute
{
    /// <summary>
    ///     Gets or sets the route template for this autocomplete endpoint.
    ///     Default: <c>api/{entity}/autocomplete/{property}</c> (all lowercase).
    /// </summary>
    public string? Route { get; set; }

    /// <summary>
    ///     Gets or sets the default result limit.
    ///     Default: 10.
    /// </summary>
    public int DefaultLimit { get; set; } = 10;
}
