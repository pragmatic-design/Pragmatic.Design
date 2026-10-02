using Pragmatic.Composition.Attributes;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Specifications;

/// <summary>
/// Tests composable specification pattern for properties.
/// Demonstrates: Spec composition with And, AndIf, negation (!), and IsPremium shorthand.
/// </summary>
/// <remarks>
/// The rules live in the other half of the entity's generated <c>PropertySpecifications</c>, beside <c>ById</c>
/// and <c>ByCode</c>, which is where the documentation tells an application to put them.
/// </remarks>
public class PropertySpecificationsTests
{
    /// <summary>
    /// The reads the generator derives from the declared rules — <c>.InCity(city)</c> on the queryable,
    /// <c>FindInCityAsync(city)</c> on the repository — are emitted in the namespace of the class that declares
    /// them: with the rules on the entity's partial, that is the entities' namespace.
    /// </summary>
    [Fact]
    public void TheDerivedReads_AreInTheEntitiesNamespace()
        => typeof(PropertySpecificationExtensions).Namespace.Should().Be("Showcase.Catalog.Entities");

    [Fact]
    public void ADeclaredRule_ComposesWithTheGeneratedKeyRule()
    {
        var spec = PropertySpecifications.ByCode("PROP-ROME") & PropertySpecifications.IsActive();
        var match = CreateProperty(isActive: true);
        match.SetCode("PROP-ROME");
        var inactive = CreateProperty(isActive: false);
        inactive.SetCode("PROP-ROME");

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(inactive).Should().BeFalse();
    }

    [Fact]
    public void IsActive_IncludesActiveProperty()
    {
        var spec = PropertySpecifications.IsActive();
        var property = CreateProperty(isActive: true);

        spec.IsSatisfiedBy(property).Should().BeTrue();
    }

    [Fact]
    public void IsActive_ExcludesInactiveProperty()
    {
        var spec = PropertySpecifications.IsActive();
        var property = CreateProperty(isActive: false);

        spec.IsSatisfiedBy(property).Should().BeFalse();
    }

    [Fact]
    public void IsActive_ExcludesSoftDeleted()
    {
        var spec = PropertySpecifications.IsActive();
        var property = CreateProperty(isActive: true);
        property.IsDeleted = true;

        spec.IsSatisfiedBy(property).Should().BeFalse();
    }

    [Fact]
    public void InCity_MatchesCorrectCity()
    {
        var spec = PropertySpecifications.InCity("Rome");
        var match = CreateProperty(city: "Rome");
        var noMatch = CreateProperty(city: "Milan");

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(noMatch).Should().BeFalse();
    }

    [Fact]
    public void MinStars_FiltersCorrectly()
    {
        var spec = PropertySpecifications.MinStars(4);
        var fiveStar = CreateProperty(starRating: 5);
        var fourStar = CreateProperty(starRating: 4);
        var threeStar = CreateProperty(starRating: 3);

        spec.IsSatisfiedBy(fiveStar).Should().BeTrue();
        spec.IsSatisfiedBy(fourStar).Should().BeTrue();
        spec.IsSatisfiedBy(threeStar).Should().BeFalse();
    }

    [Fact]
    public void NameContains_MatchesSubstring()
    {
        var spec = PropertySpecifications.NameContains("Grand");
        var match = CreateProperty(name: "Grand Hotel");
        var noMatch = CreateProperty(name: "Budget Inn");

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(noMatch).Should().BeFalse();
    }

    [Fact]
    public void NameContains_IsCaseInsensitive()
    {
        var spec = PropertySpecifications.NameContains("grand");
        var match = CreateProperty(name: "Grand Hotel");

        spec.IsSatisfiedBy(match).Should().BeTrue();
    }

    [Fact]
    public void Search_WithAllFilters_ComposesCorrectly()
    {
        var spec = PropertySpecifications.Search(city: "Rome", minStars: 4, nameSearch: "Grand");

        var match = CreateProperty(city: "Rome", starRating: 5, name: "Grand Hotel");
        var wrongCity = CreateProperty(city: "Milan", starRating: 5, name: "Grand Hotel");
        var lowStars = CreateProperty(city: "Rome", starRating: 3, name: "Grand Hotel");
        var wrongName = CreateProperty(city: "Rome", starRating: 5, name: "Budget Inn");

        spec.IsSatisfiedBy(match).Should().BeTrue();
        spec.IsSatisfiedBy(wrongCity).Should().BeFalse();
        spec.IsSatisfiedBy(lowStars).Should().BeFalse();
        spec.IsSatisfiedBy(wrongName).Should().BeFalse();
    }

    [Fact]
    public void Search_WithNoFilters_ReturnsActive()
    {
        var spec = PropertySpecifications.Search();

        var active = CreateProperty(isActive: true);
        var inactive = CreateProperty(isActive: false);

        spec.IsSatisfiedBy(active).Should().BeTrue();
        spec.IsSatisfiedBy(inactive).Should().BeFalse();
    }

    [Fact]
    public void IsPremium_RequiresActiveAndFourPlusStars()
    {
        var spec = PropertySpecifications.IsPremium();

        var premium = CreateProperty(starRating: 4, isActive: true);
        var lowStar = CreateProperty(starRating: 3, isActive: true);
        var inactive = CreateProperty(starRating: 5, isActive: false);

        spec.IsSatisfiedBy(premium).Should().BeTrue();
        spec.IsSatisfiedBy(lowStar).Should().BeFalse();
        spec.IsSatisfiedBy(inactive).Should().BeFalse();
    }

    [Fact]
    public void IsInactive_NegatesIsActive()
    {
        var spec = PropertySpecifications.IsInactive();

        var active = CreateProperty(isActive: true);
        var inactive = CreateProperty(isActive: false);
        var deleted = CreateProperty(isActive: true);
        deleted.IsDeleted = true;

        spec.IsSatisfiedBy(active).Should().BeFalse();
        spec.IsSatisfiedBy(inactive).Should().BeTrue();
        spec.IsSatisfiedBy(deleted).Should().BeTrue();
    }

    private static Property CreateProperty(
        string name = "Test Hotel",
        string city = "Rome",
        int starRating = 4,
        bool isActive = true)
    {
        var property = new Property();
        property.SetName(name);
        property.SetCity(city);
        property.SetStarRating(starRating);
        property.SetIsActive(isActive);
        property.SetCode($"PROP-{Guid.NewGuid().ToString()[..8]}");
        property.SetCountry("IT");
        return property;
    }
}
