namespace Pragmatic.Telemetry.Conventions;

/// <summary>
///     Tag names for outbound email.
/// </summary>
public static class EmailTags
{
    /// <summary>The transport used (e.g. "smtp").</summary>
    public const string Transport = "pragmatic.email.transport";

    /// <summary>Number of recipients on the message.</summary>
    public const string Recipients = "pragmatic.email.recipients";
}
