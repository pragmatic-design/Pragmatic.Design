using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

/// <summary>
///     Extension methods for configuring EF Core with Pragmatic.Internationalization support.
/// </summary>
public static class DbContextOptionsExtensions
{
    /// <summary>
    ///     Configures EF Core to use Pragmatic.Internationalization value converters and conventions.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This method is currently a marker / null-guard.</b> The actual
    ///         value-converter wiring for <c>LocalizedString</c> and translatable
    ///         entities runs inside your <c>DbContext.ConfigureConventions</c>
    ///         override via <c>ConfigurationBuilder.AddPragmaticInternationalization()</c>
    ///         (see <c>Pragmatic.Internationalization.EntityFrameworkCore.Conventions</c>).
    ///         Calling <c>UsePragmaticInternationalization</c> alone has no effect;
    ///         it is kept as the public extension entry point so callers wire it
    ///         consistently with other <c>Use*</c> Pragmatic options.
    ///     </para>
    ///     <para>
    ///         A future release may move the convention registration into this
    ///         method once EF Core exposes a clean hook from <c>DbContextOptions</c>.
    ///     </para>
    /// </remarks>
    /// <param name="optionsBuilder">The DbContext options builder.</param>
    /// <returns>The same options builder for chaining.</returns>
    /// <example>
    ///     <code>
    /// protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    /// {
    ///     optionsBuilder
    ///         .UseSqlServer(connectionString)
    ///         .UsePragmaticInternationalization();
    /// }
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UsePragmaticInternationalization(this DbContextOptionsBuilder optionsBuilder)
    {
        Ensure.Ensure.ThrowIfNull(optionsBuilder);

        // Note: EF Core 8+ uses conventions for value converters.
        // The actual configuration happens in OnModelCreating via ConfigureConventions.
        return optionsBuilder;
    }
}