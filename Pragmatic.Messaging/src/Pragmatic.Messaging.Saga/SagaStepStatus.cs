namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Status of an individual saga step.
/// </summary>
public enum SagaStepStatus
{
    /// <summary>Step is waiting to execute.</summary>
    Pending = 0,

    /// <summary>Step completed successfully.</summary>
    Completed = 1,

    /// <summary>Step failed.</summary>
    Failed = 2,

    /// <summary>Step was compensated (rolled back).</summary>
    Compensated = 3
}
