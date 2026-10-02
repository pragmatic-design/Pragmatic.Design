using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Pragmatic.Temporal.EntityFrameworkCore;

/// <summary>
///     Extension methods for configuring EF Core with Pragmatic.Temporal.
/// </summary>
public static class DbContextOptionsExtensions
{
    /// <summary>
    ///     Configures EF Core to use Pragmatic.Temporal conventions.
    ///     Automatically applies value converters for temporal types.
    /// </summary>
    /// <param name="builder">The options builder.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The builder for chaining.</returns>
    public static DbContextOptionsBuilder UsePragmaticTemporal(
        this DbContextOptionsBuilder builder,
        Action<TemporalEfCoreOptions>? configure = null)
    {
        // Compose on the options of a previous UsePragmaticTemporal call, if any
        var options = builder.Options.FindExtension<TemporalOptionsExtension>()?.Options
                      ?? new TemporalEfCoreOptions();
        configure?.Invoke(options);

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TemporalOptionsExtension(options));

        return builder;
    }
}
