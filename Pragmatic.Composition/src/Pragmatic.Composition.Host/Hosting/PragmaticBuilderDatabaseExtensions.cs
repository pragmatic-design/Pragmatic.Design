namespace Pragmatic.Composition.Hosting;

/// <summary>
///     Extension methods for configuring database initialization on <see cref="IPragmaticBuilder" />.
/// </summary>
public static class PragmaticBuilderDatabaseExtensions
{
    extension(IPragmaticBuilder builder)
    {
        /// <summary>
        ///     Enables automatic database creation via EF Core EnsureCreated on startup.
        ///     Creates tables from the model without migrations — intended for development only.
        /// </summary>
        public IPragmaticBuilder UseDatabaseEnsureCreated()
        {
            if (builder is not PragmaticBuilder pb)
                throw new NotSupportedException(
                    $"{nameof(UseDatabaseEnsureCreated)} requires the built-in {nameof(PragmaticBuilder)}; " +
                    $"the custom {builder.GetType().Name} does not expose the database options this method sets. " +
                    "Set the database strategy on your own builder directly.");

            if (pb.Options.AutoMigrations)
                throw new InvalidOperationException(
                    "Cannot enable EnsureDatabaseCreated when AutoMigrations is already active. " +
                    "The two options are mutually exclusive.");
            pb.Options.EnsureDatabaseCreated = true;
            return builder;
        }

        /// <summary>
        ///     Enables automatic EF Core migrations on startup.
        ///     Applies pending migrations — intended for production.
        /// </summary>
        public IPragmaticBuilder UseDatabaseMigrate()
        {
            if (builder is not PragmaticBuilder pb)
                throw new NotSupportedException(
                    $"{nameof(UseDatabaseMigrate)} requires the built-in {nameof(PragmaticBuilder)}; " +
                    $"the custom {builder.GetType().Name} does not expose the database options this method sets. " +
                    "Set the database strategy on your own builder directly.");

            if (pb.Options.EnsureDatabaseCreated)
                throw new InvalidOperationException(
                    "Cannot enable AutoMigrations when EnsureDatabaseCreated is already active. " +
                    "The two options are mutually exclusive.");
            pb.Options.AutoMigrations = true;
            return builder;
        }
    }
}
