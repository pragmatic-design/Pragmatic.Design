namespace Pragmatic.Composition.Scanning;

/// <summary>
///     Defines how to handle duplicate service registrations during assembly scanning.
/// </summary>
public enum RegistrationStrategy
{
    /// <summary>
    ///     Appends a new registration even if the service was already registered (default).
    /// </summary>
    Append,

    /// <summary>
    ///     Skips registration if the service type is already registered.
    /// </summary>
    Skip,

    /// <summary>
    ///     Replaces any existing registration for the service type.
    /// </summary>
    Replace,

    /// <summary>
    ///     Throws an exception if a duplicate registration is detected.
    /// </summary>
    Throw
}
