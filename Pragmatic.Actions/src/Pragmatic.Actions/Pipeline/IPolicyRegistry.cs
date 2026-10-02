using Pragmatic.Authorization.Policy;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Registry for compile-time resolved action policies.
///     Source-generated implementation maps action types to their <see cref="ResourcePolicy" /> instances.
/// </summary>
public interface IPolicyRegistry
{
    /// <summary>
    ///     Gets the policy for the specified action type, or null if no policy is required.
    /// </summary>
    ResourcePolicy? GetPolicy(Type actionType);
}
