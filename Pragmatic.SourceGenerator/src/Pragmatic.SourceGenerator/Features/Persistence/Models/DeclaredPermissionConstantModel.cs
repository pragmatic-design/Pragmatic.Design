namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A declared permission's constant in the <c>{Boundary}Permissions</c> class — from
///     <c>[assembly: Permission]</c> or a <c>[RequirePermission(Description = …)]</c> — already checked: its
///     first segment a boundary, its path and its name free.
/// </summary>
/// <param name="Value">The permission's value — <c>{boundary}.{resource}.{verb}</c>.</param>
/// <param name="Description">What holding it allows, for the constant's summary.</param>
internal sealed record DeclaredPermissionConstantModel(string Value, string? Description);
