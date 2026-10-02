namespace Pragmatic.Actions.Configuration;

/// <summary>
///     Options for configuring Pragmatic.Actions behavior.
/// </summary>
public sealed class PragmaticActionsOptions
{
    /// <summary>
    ///     Gets or sets whether the built-in logging filter is enabled.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     The logging filter logs action execution at Debug level and results at
    ///     Information/Warning level. Disable if you prefer to use only your own
    ///     logging or if the ActionPipeline's internal logging is sufficient.
    /// </remarks>
    public bool EnableLoggingFilter { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether the built-in validation filter is enabled.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The validation filter validates DomainActions marked with <c>[Validate]</c>
    ///         before execution. It runs both sync validation (from attributes) and async
    ///         validation (from registered <c>IValidator&lt;T&gt;</c>).
    ///     </para>
    ///     <para>
    ///         Requires <c>Pragmatic.Validation</c> to be configured for sync validation
    ///         and validators to be registered for async validation.
    ///     </para>
    /// </remarks>
    public bool EnableValidationFilter { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether the built-in permission authorization filter is enabled.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     The permission filter enforces <c>[RequirePermission]</c> and <c>[RequireAnyPermission]</c>
    ///     attributes on DomainActions. Requires <c>ICurrentUser</c> to be registered.
    ///     Runs at Order 200 (after validation at 100, before transaction at 300).
    /// </remarks>
    public bool EnablePermissionFilter { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether the built-in resource authorization filter is enabled.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     The resource authorization filter enforces <c>IResourceAuthorizer&lt;T&gt;</c>
    ///     on DomainActions. Runs at Order 250 (after permission check at 200, before transaction at 300).
    ///     Only activates when an authorizer is registered for the action type.
    /// </remarks>
    public bool EnableResourceAuthorizationFilter { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether the built-in policy evaluation filter is enabled.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     The policy evaluation filter enforces <c>[RequirePolicy&lt;T&gt;]</c>
    ///     on DomainActions. Runs at Order 210 (after permission check at 200, before resource authorization at 250).
    /// </remarks>
    public bool EnablePolicyFilter { get; set; } = true;
}
