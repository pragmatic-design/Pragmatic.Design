namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for Pragmatic.Actions instrumentation.
/// </summary>
public static class ActionTags
{
    /// <summary>The action class full name (e.g., "MyApp.CreateOrder").</summary>
    public const string Name = "pragmatic.action.name";

    /// <summary>Action kind: "domain_action", "void_action", "mutation".</summary>
    public const string Kind = "pragmatic.action.kind";

    /// <summary>Outcome: "success", "failure", "short_circuited".</summary>
    public const string Result = "pragmatic.action.result";

    /// <summary>Error code when result is failure.</summary>
    public const string ErrorCode = "pragmatic.action.error_code";

    /// <summary>Mutation mode: "create", "update", "delete", "restore".</summary>
    public const string MutationMode = "pragmatic.mutation.mode";

    /// <summary>Entity type being mutated.</summary>
    public const string EntityType = "pragmatic.mutation.entity_type";

    /// <summary>Number of filters in the pipeline.</summary>
    public const string FilterCount = "pragmatic.action.filter_count";

    /// <summary>Set when a post-commit step threw after the transaction committed.</summary>
    public const string PostCommitFailed = "pragmatic.action.post_commit_failed";

    /// <summary>
    ///     Set when an action failed and undoing an inner boundary's committed work failed too, so
    ///     the request ended with the system inconsistent rather than merely unchanged.
    /// </summary>
    public const string CompensationFailed = "pragmatic.action.compensation_failed";

    /// <summary>The mutation was refused by the permission check.</summary>
    public const string PermissionDenied = "pragmatic.mutation.permission.denied";

    /// <summary>The mutation was refused by policy evaluation.</summary>
    public const string PolicyDenied = "pragmatic.mutation.policy.denied";

    /// <summary>The mutation was refused by a data filter.</summary>
    public const string FilterDenied = "pragmatic.mutation.filter.denied";

    /// <summary>The mutation was refused by resource authorization.</summary>
    public const string ResourceDenied = "pragmatic.mutation.resource.denied";
}
