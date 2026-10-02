namespace Casework.Intake.Dtos;

/// <summary>
///     What an operator gets back after asking for an organisation: mostly, where the process is.
/// </summary>
/// <remarks>
///     ⚠️ <b>No connection string.</b> It is on the row and it is a secret; the operator asked for an
///     organisation, not for where its rows live.
/// </remarks>
/// <param name="TenantKey">The id its people's tokens will carry.</param>
/// <param name="Name">What it is called.</param>
/// <param name="State">
///     <c>Provisioning</c> when this answer is written: the other service has still to make its own
///     database, and until it says so this organisation is refused by both.
/// </param>
public sealed record OrganisationDto(string TenantKey, string Name, string State);
