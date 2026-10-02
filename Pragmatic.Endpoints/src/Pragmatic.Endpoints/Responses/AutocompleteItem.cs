namespace Pragmatic.Endpoints.Responses;

/// <summary>
///     A single autocomplete result with a strongly-typed identifier and display value.
///     Used as the return type for auto-generated autocomplete endpoints.
/// </summary>
/// <typeparam name="TKey">The type of the entity's primary key (e.g., Guid, int).</typeparam>
public sealed record AutocompleteItem<TKey>(TKey Id, string Value);
