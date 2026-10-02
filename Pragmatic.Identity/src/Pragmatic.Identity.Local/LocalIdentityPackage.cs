using Pragmatic.Composition;

namespace Pragmatic.Identity.Local;

/// <summary>
///     Self-hosted identity provider package. Manages credentials locally with
///     password hashing, login, registration, and password reset.
/// </summary>
public sealed class LocalIdentityPackage : IPackageDefinition
{
    /// <inheritdoc />
    public static string PackageName => "Pragmatic.Identity.Local";

    /// <inheritdoc />
    public static string? RoutePrefix => "identity/local";

    /// <inheritdoc />
    public static string? Description => "Self-hosted identity with local credentials";
}
