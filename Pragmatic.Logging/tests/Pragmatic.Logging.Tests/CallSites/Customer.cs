using Pragmatic.Privacy;

namespace Pragmatic.Logging.Tests.CallSites;

/// <summary>An application type a call site logs whole, with a member it declared personal data.</summary>
/// <remarks>
///     It reaches a second declaration one level down (<see cref="Address.Street" />), carries an enum and a
///     nullable member, so the writer is checked against the classic path on more than a flat record.
/// </remarks>
public sealed record Customer(
    string Reference,
    [property: PersonalData(DataCategory.Contact)] string Email,
    CustomerTier Tier,
    Address? Shipping);
