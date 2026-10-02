namespace Pragmatic.Identity;

/// <summary>
///     Identifies the type of principal making the current request.
/// </summary>
public enum PrincipalKind
{
    /// <summary>No authenticated identity.</summary>
    Anonymous,

    /// <summary>A human user authenticated via an identity provider.</summary>
    User,

    /// <summary>A service-to-service caller (e.g., API key, client credentials).</summary>
    Service,

    /// <summary>The system itself (background jobs, seed, migrations).</summary>
    System
}
