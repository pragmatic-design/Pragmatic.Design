namespace Pragmatic.Composition;

/// <summary>
///     Defines a package that can be imported into a module via <c>[UsePackage&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
///     <para>
///         A package is a pre-built collection of entities, actions, and services that integrates
///         into a host module's boundary. Unlike a module/boundary (which is a self-contained unit),
///         a package's metadata are fused into the target module at compile-time.
///     </para>
///     <para>
///         Typical packages include identity providers (Local, Keycloak), payment integrations,
///         or any reusable infrastructure that needs to compose into the application's domain model.
///     </para>
/// </remarks>
public interface IPackageDefinition
{
    /// <summary>
    ///     Gets the package name used for DI registration, diagnostics, and topology reports.
    /// </summary>
    static abstract string PackageName { get; }

    /// <summary>
    ///     Gets the route prefix for package endpoints (e.g., "identity/local").
    ///     Null if the package does not expose endpoints.
    /// </summary>
    static abstract string? RoutePrefix { get; }

    /// <summary>
    ///     Gets an optional description for generated topology reports.
    /// </summary>
    static abstract string? Description { get; }
}
