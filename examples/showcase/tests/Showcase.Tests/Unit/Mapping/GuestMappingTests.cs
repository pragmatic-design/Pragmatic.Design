using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;
using Showcase.Booking.Dtos;
using Showcase.Booking.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Mapping;

/// <summary>
/// Tests source-generated mapping between Guest entity and DTOs.
/// Demonstrates: [MapFrom] generates FromEntity(),
/// [MapIgnore] on computed FullName, [GenerateProjection] for IQueryable.
/// </summary>
public class GuestMappingTests
{
    /// <summary>
    ///     The whole DTO, compared member by member against the one the mapping should produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[assembly: GenerateComparer&lt;GuestDto&gt;]</c> in
    ///         <c>MockDeclarations.cs</c> generates this <c>BeEquivalentTo</c>. It says <b>where</b>
    ///         two values differ — <c>Expected dto.LastName to be "Doe", but found "Roe"</c> —
    ///         where <c>Equals</c> on a DTO with no value equality would compare references and
    ///         fail on two identical objects.
    ///     </para>
    ///     <para>
    ///         ⚠️ The expected DTO is written out by hand and not produced by the mapper: built the
    ///         other way this would compare the mapping with itself. And it covers every member,
    ///         <c>PreferredLanguage</c> among them — the kind of member a list of hand-written
    ///         assertions leaves out.
    ///     </para>
    /// </remarks>
    [Fact]
    public void GuestDto_FromEntity_MapsEveryMember()
    {
        var guest = CreateGuest();

        var dto = GuestDto.FromEntity(guest);

        dto.Should().BeEquivalentTo(new GuestDto
        {
            Id = guest.Id,
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Phone = "+43123456789",
            Nationality = "AT",
            PreferredLanguage = "de",
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt
        });
    }

    [Fact]
    public void GuestDto_FullName_IsComputed()
    {
        var guest = CreateGuest();

        var dto = GuestDto.FromEntity(guest);

        dto.FullName.Should().Be("John Doe");
    }

    [Fact]
    public void GuestDto_Projection_IsNotNull()
    {
        // [GenerateProjection] creates a static Projection expression
        GuestDto.Projection.Should().NotBeNull();
    }

    private static Guest CreateGuest()
    {
        var guest = new Guest();
        guest.SetFirstName("John");
        guest.SetLastName("Doe");
        guest.SetEmail("john@example.com");
        guest.SetPhone("+43123456789");
        guest.SetNationality("AT");
        guest.SetPreferredLanguage("de");
        return guest;
    }
}
