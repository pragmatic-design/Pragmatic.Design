using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO for updating an address.
///     Used as a nested patch inside UpdateOrderWithAddressDto.
/// </summary>
[Patch<Address>]
public partial class UpdateAddressDto
{
    /// <summary>
    ///     Updated street line 1.
    /// </summary>
    public string? Street1 { get; init; }

    /// <summary>
    ///     Updated street line 2.
    /// </summary>
    public string? Street2 { get; init; }

    /// <summary>
    ///     Updated city.
    /// </summary>
    public string? City { get; init; }

    /// <summary>
    ///     Updated state.
    /// </summary>
    public string? State { get; init; }

    /// <summary>
    ///     Updated postal code.
    /// </summary>
    public string? PostalCode { get; init; }

    /// <summary>
    ///     Updated country.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    ///     Updated default flag.
    /// </summary>
    public bool? IsDefault { get; init; }

    /// <summary>
    ///     Updated address type.
    /// </summary>
    public AddressType? Type { get; init; }
}
