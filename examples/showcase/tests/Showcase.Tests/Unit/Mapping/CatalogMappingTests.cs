using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Dtos;
using Showcase.Catalog.Entities;
using Showcase.Catalog.Enums;
using Xunit;

namespace Showcase.Tests.Unit.Mapping;

/// <summary>
/// Tests source-generated mapping for Catalog DTOs (RoomType, CancellationPolicy, Amenity).
/// Demonstrates: [MapFrom], [MapProperty] navigation flattening, [GenerateProjection].
/// </summary>
public class CatalogMappingTests
{
    [Fact]
    public void RoomTypeSummaryDto_FromEntity_MapsAllFields()
    {
        var roomType = CreateRoomType();

        var dto = RoomTypeSummaryDto.FromEntity(roomType);

        dto.Id.Should().Be(roomType.Id);
        dto.Name.Should().Be("Deluxe Suite");
        dto.Code.Should().Be("DLX");
        dto.MaxOccupancy.Should().Be(4);
        dto.BaseRate.Should().Be(299.99m);
        dto.Currency.Should().Be("EUR");
        dto.TotalRooms.Should().Be(10);
    }

    [Fact]
    public void RoomTypeSummaryDto_FromEntity_FlattensPropertyName()
    {
        var roomType = new RoomType();
        var property = new Property();
        property.SetName("Grand Hotel");
        roomType.Property = property;

        var dto = RoomTypeSummaryDto.FromEntity(roomType);

        dto.PropertyName.Should().Be("Grand Hotel");
    }

    [Fact]
    public void CancellationPolicyDto_FromEntity_MapsAllFields()
    {
        var property = new Property();
        property.SetName("Test Property");

        var policy = new CancellationPolicy();
        policy.SetPropertyId(Guid.NewGuid());
        policy.SetName("Flexible");
        policy.SetHoursBeforeCheckIn(24);
        policy.SetPenaltyPercentage(10m);
        policy.SetIsDefault(true);
        policy.Property = property;

        var dto = CancellationPolicyDto.FromEntity(policy);

        dto.Name.Should().Be("Flexible");
        dto.HoursBeforeCheckIn.Should().Be(24);
        dto.PenaltyPercentage.Should().Be(10m);
        dto.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void CancellationPolicyDto_FromEntity_FlattensPropertyName()
    {
        var property = new Property();
        property.SetName("Seaside Resort");

        var policy = new CancellationPolicy();
        policy.Property = property;

        var dto = CancellationPolicyDto.FromEntity(policy);

        dto.PropertyName.Should().Be("Seaside Resort");
    }

    [Fact]
    public void AmenityDto_FromEntity_MapsAllFields()
    {
        var amenity = new Amenity();
        amenity.SetName("Swimming Pool");
        amenity.SetCategory(AmenityCategory.Pool);
        amenity.SetIconName("pool");

        var dto = AmenityDto.FromEntity(amenity);

        dto.Id.Should().Be(amenity.Id);
        dto.Name.Should().Be("Swimming Pool");
        dto.Category.Should().Be(AmenityCategory.Pool);
        dto.IconName.Should().Be("pool");
    }

    [Fact]
    public void RoomTypeSummaryDto_Projection_IsNotNull()
    {
        RoomTypeSummaryDto.Projection.Should().NotBeNull();
    }

    [Fact]
    public void CancellationPolicyDto_Projection_IsNotNull()
    {
        CancellationPolicyDto.Projection.Should().NotBeNull();
    }

    [Fact]
    public void AmenityDto_Projection_IsNotNull()
    {
        AmenityDto.Projection.Should().NotBeNull();
    }

    [Fact]
    public void PropertyDetailDto_Projection_IsNotNull()
    {
        PropertyDetailDto.Projection.Should().NotBeNull();
    }

    private static RoomType CreateRoomType()
    {
        var property = new Property();
        property.SetName("Test Property");

        var roomType = new RoomType();
        roomType.SetPropertyId(Guid.NewGuid());
        roomType.SetName("Deluxe Suite");
        roomType.SetCode("DLX");
        roomType.SetMaxOccupancy(4);
        roomType.SetBaseRate(299.99m);
        roomType.SetCurrency("EUR");
        roomType.SetTotalRooms(10);
        roomType.Property = property;
        return roomType;
    }
}
