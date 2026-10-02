using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

/// <summary>
///     Extension methods for configuring EF Core model with Pragmatic.Internationalization types.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    ///     Applies Pragmatic.Internationalization conventions to the model.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder for chaining.</returns>
    /// <remarks>
    ///     This method configures:
    ///     <list type="bullet">
    ///         <item>CurrencyCode properties to use varchar(3)</item>
    ///         <item>LocalizedString properties to use a JSON column conversion</item>
    ///     </list>
    /// </remarks>
    /// <example>
    ///     <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.ApplyPragmaticInternationalization();
    ///     // ... other configuration
    /// }
    /// </code>
    /// </example>
    public static ModelBuilder ApplyPragmaticInternationalization(this ModelBuilder modelBuilder)
    {
        Ensure.Ensure.ThrowIfNull(modelBuilder);

        // Configure all CurrencyCode and LocalizedString properties.
        // LocalizedString is a reference type, so typeof(LocalizedString) also matches nullable
        // (LocalizedString?) properties and a single converter covers both.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                if (property.ClrType == typeof(CurrencyCode))
                {
                    property.SetMaxLength(3);
                    property.SetValueConverter(new CurrencyCodeValueConverter());
                }
                else if (property.ClrType == typeof(CurrencyCode?))
                {
                    property.SetMaxLength(3);
                    property.SetValueConverter(new NullableCurrencyCodeValueConverter());
                }
                else if (property.ClrType == typeof(LocalizedString))
                {
                    property.SetValueConverter(new LocalizedStringValueConverter());
                }

        return modelBuilder;
    }

    /// <summary>
    ///     Applies Pragmatic.Internationalization conventions using ConfigureConventions (EF Core 6+).
    /// </summary>
    /// <param name="configurationBuilder">The model configuration builder.</param>
    /// <example>
    ///     <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    /// {
    ///     configurationBuilder.ApplyPragmaticInternationalizationConventions();
    /// }
    /// </code>
    /// </example>
    public static void ApplyPragmaticInternationalizationConventions(
        this ModelConfigurationBuilder configurationBuilder)
    {
        Ensure.Ensure.ThrowIfNull(configurationBuilder);

        // Configure CurrencyCode type globally
        configurationBuilder
            .Properties<CurrencyCode>()
            .HaveMaxLength(3)
            .HaveConversion<CurrencyCodeValueConverter>();

        configurationBuilder
            .Properties<CurrencyCode?>()
            .HaveMaxLength(3)
            .HaveConversion<NullableCurrencyCodeValueConverter>();

        // Configure LocalizedString type globally (JSON column). Reference type → covers nullable too.
        configurationBuilder
            .Properties<LocalizedString>()
            .HaveConversion<LocalizedStringValueConverter>();
    }
}