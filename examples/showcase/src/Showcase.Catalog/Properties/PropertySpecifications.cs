namespace Showcase.Catalog.Entities;

/// <summary>
/// Composable specification factory for hotel properties.
/// Demonstrates: Spec&lt;T&gt;.Where(), operator composition (&amp; | !), AndIf/OrIf.
/// </summary>
/// <remarks>
///     The other half of the generated <c>PropertySpecifications</c>, which holds <c>ById</c> and <c>ByCode</c>: one
///     class per entity, in the entities' namespace, so every file that sees <see cref="Property" /> sees its rules
///     too. The generator also turns each member into a read of its own — <c>.InCity(city)</c> on the queryable,
///     <c>FindInCityAsync(city)</c> on the repository.
/// </remarks>
public static partial class PropertySpecifications
{
    /// <summary>Active and not soft-deleted.</summary>
    public static Specification<Property> IsActive()
        => Spec<Property>.Where(p => p.IsActive && !p.IsDeleted);

    /// <summary>Located in a specific city.</summary>
    public static Specification<Property> InCity(string city)
        => Spec<Property>.Where(p => p.City == city);

    /// <summary>Has at least the given star rating.</summary>
    public static Specification<Property> MinStars(int minStars)
        => Spec<Property>.Where(p => p.StarRating >= minStars);

    /// <summary>Name contains search term (case-insensitive).</summary>
    public static Specification<Property> NameContains(string term)
        => Spec<Property>.Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a dynamic search spec from optional filters.
    /// Demonstrates conditional composition with AndIf.
    /// </summary>
    public static Specification<Property> Search(
        string? city = null,
        int? minStars = null,
        string? nameSearch = null)
    {
        return IsActive()
            .AndIf(city is not null, InCity(city!))
            .AndIf(minStars.HasValue, MinStars(minStars ?? 0))
            .AndIf(nameSearch is not null, NameContains(nameSearch!));
    }

    /// <summary>
    /// Premium properties: active AND 4+ stars.
    /// Demonstrates operator composition.
    /// </summary>
    public static Specification<Property> IsPremium()
        => IsActive() & MinStars(4);

    /// <summary>
    /// Inactive or soft-deleted properties.
    /// Demonstrates NOT operator.
    /// </summary>
    public static Specification<Property> IsInactive()
        => !IsActive();
}
