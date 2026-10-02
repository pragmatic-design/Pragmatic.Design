namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Registry for compile-time resolved permission requirements.
///     Source-generated implementation maps action types to their permission requirements,
///     eliminating <c>Attribute.GetCustomAttribute</c> reflection at runtime.
/// </summary>
public interface IPermissionRequirementRegistry
{
    /// <summary>
    ///     Gets the permission requirement for the specified action type, or null if none.
    /// </summary>
    PermissionRequirementEntry? GetRequirement(Type actionType);
}
