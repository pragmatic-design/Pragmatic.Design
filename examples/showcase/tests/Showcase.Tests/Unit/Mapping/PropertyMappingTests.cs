using Pragmatic.Testing.Assertions;
using Pragmatic.Internationalization.Types;
using Showcase.Catalog.Dtos;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Mapping;

/// <summary>
/// Tests source-generated mapping between Property entity and DTOs.
/// Demonstrates: [MapFrom] generates FromEntity(), [MapIgnore] skips computed properties.
/// </summary>
public class PropertyMappingTests
{
    [Fact]
    public void PropertySummaryDto_FromEntity_MapsAllFields()
    {
        var property = CreateProperty();

        var dto = PropertySummaryDto.FromEntity(property);

        dto.Id.Should().Be(property.Id);
        dto.Code.Should().Be("PROP-001");
        dto.Name.Should().Be("Grand Hotel");
        dto.City.Should().Be("Vienna");
        dto.Country.Should().Be("Austria");
        dto.StarRating.Should().Be(5);
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public void PropertyDetailDto_FromEntity_MapsAllFields()
    {
        var property = CreateProperty();

        var dto = PropertyDetailDto.FromEntity(property);

        dto.Id.Should().Be(property.Id);
        dto.Description.Should().Be("A luxury hotel");
        dto.Address.Should().Be("Ringstrasse 1");
        dto.TimeZone.Should().Be("Europe/Vienna");
        dto.CheckInTime.Should().Be(new TimeOnly(14, 0));
        dto.CheckOutTime.Should().Be(new TimeOnly(11, 0));
    }

    [Fact]
    public void PropertyDetailDto_FromEntity_FormatsCreatedDate()
    {
        var property = CreateProperty();
        // CreatedAt is IAuditable — public set
        property.CreatedAt = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        var dto = PropertyDetailDto.FromEntity(property);

        dto.CreatedDate.Should().Be("2026-01-15");
    }

    [Fact]
    public void PropertyDetailDto_DisplayLabel_IsComputedNotMapped()
    {
        var property = CreateProperty();

        var dto = PropertyDetailDto.FromEntity(property);

        dto.DisplayLabel.Should().Be("Grand Hotel (Vienna, 5★)");
    }

    [Fact]
    public void PropertyDetailDto_MapCondition_MapsTimeZoneOnlyWhenActive()
    {
        // [MapCondition(nameof(ShouldMapOperationalDetails))]: TimeZone maps only for active properties.
        var active = CreateProperty();
        PropertyDetailDto.FromEntity(active).TimeZone.Should().Be("Europe/Vienna");

        var inactive = CreateProperty();
        inactive.SetIsActive(false);
        PropertyDetailDto.FromEntity(inactive).TimeZone.Should().BeNull(
            "the [MapCondition] predicate is false, so the property keeps its default");
    }

    private static Property CreateProperty()
    {
        var property = new Property();
        property.SetCode("PROP-001");
        property.SetName("Grand Hotel");
        property.SetDescription(LocalizedString.From("A luxury hotel"));
        property.SetAddress("Ringstrasse 1");
        property.SetCity("Vienna");
        property.SetCountry("Austria");
        property.SetStarRating(5);
        property.SetTimeZone("Europe/Vienna");
        property.SetCheckInTime(new TimeOnly(14, 0));
        property.SetCheckOutTime(new TimeOnly(11, 0));
        property.SetIsActive(true);
        property.CreatedAt = DateTimeOffset.UtcNow;
        return property;
    }
}
