using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Actions.Boundary;

/// <summary>
///     Fluent configuration for a bounded context.
/// </summary>
/// <typeparam name="TBoundary">The boundary marker type.</typeparam>
/// <remarks>
///     <para>
///         Use this class to configure how a boundary is accessed at runtime.
///         Boundaries can be either local (in-process with DbContext) or remote (via HTTP).
///     </para>
///     <para>
///         For database configuration, use the <c>UseDatabase</c> extension method
///         from <c>Pragmatic.Actions.EFCore</c>.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Local boundary with PostgreSQL (requires Pragmatic.Actions.EFCore)
/// services.AddStudentsModule(cfg => cfg
///     .UseLocal()
///     .UseDatabase(opt => opt.UseNpgsql(connectionString)));
///
/// // Remote boundary via HTTP
/// services.AddEnrollmentModule(cfg => cfg
///     .UseRemote("https://enrollment-api.example.com"));
/// </code>
/// </example>
public sealed class BoundaryConfiguration<TBoundary> where TBoundary : IBoundary
{
    /// <summary>
    ///     Gets the boundary mode (Local or Remote).
    /// </summary>
    public BoundaryMode Mode { get; private set; } = BoundaryMode.Local;

    /// <summary>
    ///     Gets the base URL for remote boundaries.
    /// </summary>
    /// <remarks>
    ///     Only set when <see cref="Mode" /> is <see cref="BoundaryMode.Remote" />.
    /// </remarks>
    public string? RemoteBaseUrl { get; private set; }

    /// <summary>
    ///     Gets the database configuration action for local boundaries (opaque delegate).
    /// </summary>
    /// <remarks>
    ///     Set via the <c>UseDatabase</c> extension method from <c>Pragmatic.Actions.EFCore</c>.
    ///     The actual type is <c>Action&lt;DbContextOptionsBuilder&gt;</c> when using EF Core.
    /// </remarks>
    public Delegate? DatabaseOptions { get; internal set; }

    /// <summary>
    ///     Gets the boundary type.
    /// </summary>
    public Type BoundaryType => typeof(TBoundary);

    /// <summary>
    ///     Configures the boundary to run in-process with direct database access.
    /// </summary>
    /// <returns>This configuration instance for fluent chaining.</returns>
    public BoundaryConfiguration<TBoundary> UseLocal()
    {
        Mode = BoundaryMode.Local;
        RemoteBaseUrl = null;
        return this;
    }

    /// <summary>
    ///     Configures the boundary to be accessed via HTTP API calls.
    /// </summary>
    /// <param name="baseUrl">The base URL of the remote API.</param>
    /// <returns>This configuration instance for fluent chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when baseUrl is null or empty.</exception>
    public BoundaryConfiguration<TBoundary> UseRemote(string baseUrl)
    {
        ThrowIfNullOrWhiteSpace(baseUrl);

        Mode = BoundaryMode.Remote;
        RemoteBaseUrl = baseUrl.TrimEnd('/');
        DatabaseOptions = null;
        return this;
    }

    /// <summary>
    ///     Validates the configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid.</exception>
    internal void Validate()
    {
        if (Mode == BoundaryMode.Local && DatabaseOptions is null)
            throw new InvalidOperationException(
                $"Local boundary '{typeof(TBoundary).Name}' requires database configuration. " +
                "Call UseDatabase() to configure the database provider (requires Pragmatic.Actions.EFCore).");

        if (Mode == BoundaryMode.Remote && string.IsNullOrEmpty(RemoteBaseUrl))
            throw new InvalidOperationException(
                $"Remote boundary '{typeof(TBoundary).Name}' requires a base URL. " +
                "Call UseRemote(baseUrl) with a valid URL.");
    }
}
