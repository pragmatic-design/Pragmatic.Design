namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A single generated permission constant and the string value it expands to — e.g.
///     <c>BookingPermissions.GuestPreferences.Update</c> → <c>"booking.guestpreferences.update"</c>.
///     Value-equatable so it can flow through the incremental pipeline safely.
/// </summary>
/// <remarks>
///     A source generator cannot resolve the constants it generates itself in the same compilation,
///     so <c>[RequirePermission(BookingPermissions.GuestPreferences.Update)]</c> would otherwise fail
///     to extract a value and the permission would silently not be enforced (fail-open). This catalog,
///     built from the same entity metadata that generates the constants, lets the Actions feature
///     resolve those references by matching the attribute-argument syntax path to <see cref="ConstPath"/>.
/// </remarks>
internal readonly record struct PermissionConstEntry(string ConstPath, string Value);
