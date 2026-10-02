using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Patch;

namespace Pragmatic.Persistence.EFCore.Samples.Mutations;

/// <summary>
///     Patch DTO for updating a customer.
///     Demonstrates:
///     - [Patch] attribute for generating ApplyTo method
///     - Nullable properties for partial updates
///     - Nested patches for addresses
/// </summary>
[Patch<Customer>]
public partial class UpdateCustomerDto
{
    /// <summary>
    ///     Updated customer name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    ///     Updated phone number.
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    ///     Updated customer type.
    /// </summary>
    public CustomerType? Type { get; init; }

    /// <summary>
    ///     Updated active status.
    /// </summary>
    public bool? IsActive { get; init; }
}
