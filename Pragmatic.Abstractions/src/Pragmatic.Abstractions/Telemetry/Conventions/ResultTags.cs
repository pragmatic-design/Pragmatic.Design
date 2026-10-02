namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for Result-to-HTTP translation.
/// </summary>
public static class ResultTags
{
    /// <summary>Whether the result was a success.</summary>
    public const string IsSuccess = "pragmatic.result.is_success";

    /// <summary>The error code carried by a failed result.</summary>
    public const string ErrorCode = "pragmatic.result.error_code";
}
