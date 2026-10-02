namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Describes the permission requirement for a single action type.
/// </summary>
/// <param name="Permissions">Required permission names.</param>
/// <param name="RequireAll">
///     <c>true</c> for AND (all must be present), <c>false</c> for ANY (at least one).
/// </param>
public sealed record PermissionRequirementEntry(string[] Permissions, bool RequireAll);
