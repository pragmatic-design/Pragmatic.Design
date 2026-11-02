using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Repository;

/// <summary>
///     The rows an autocomplete answers, on an engine that compiles its reads: the Sql and documents engines' repositories implement it with
///     one compiled read per <c>[Autocomplete]</c> property of the entity, and the generated autocomplete endpoint names the property.
/// </summary>
/// <remarks>
///     EF Core's repository does not implement it: on EF Core the endpoint reads through <c>Query()</c>, which an engine that compiles its reads
///     does not serve. A read the endpoint built itself, a <c>Spec&lt;T&gt;.Where</c> in generated code, would reach the engine uncompiled.
/// </remarks>
/// <typeparam name="TEntity">The entity type.</typeparam>
public interface IAutocompleteReadRepository<TEntity>
    where TEntity : class, IEntity
{
    /// <summary>
    ///     At most <paramref name="take" /> rows whose <paramref name="property" /> contains <paramref name="search" />, inside the caller's
    ///     filters; every row's when <paramref name="search" /> is null or empty.
    /// </summary>
    /// <param name="property">The <c>[Autocomplete]</c> property: the key of a read the engine compiled.</param>
    /// <param name="search">The text the property contains.</param>
    /// <param name="take">The most rows answered.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<List<TEntity>> AutocompleteAsync(string property, string? search, int take, CancellationToken ct = default);
}
