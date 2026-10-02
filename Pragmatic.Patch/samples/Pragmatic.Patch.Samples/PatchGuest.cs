using Pragmatic.Patch.Attributes;

namespace Pragmatic.Patch.Samples;

/// <summary>
///     Patch DTO for Guest. SG generates Optional properties for:
///     FirstName, LastName, Email, Phone, VipLevel.
///     Excluded: Id (identity), CreatedAt/CreatedBy/UpdatedAt/UpdatedBy (audit).
/// </summary>
[GeneratePatch<Guest>]
public partial record PatchGuest;
