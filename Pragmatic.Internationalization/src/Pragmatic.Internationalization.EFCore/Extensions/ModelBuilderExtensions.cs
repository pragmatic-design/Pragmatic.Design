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
    ///         <item>LocalizedString properties to use a JSON column conversion, compared by value so a change in place is saved</item>
    ///     </list>
    ///     A <c>LocalizedString</c> property needs no declaration of its own: one EF Core's conventions took for a navigation is declared
    ///     as a property first. Call it after the entity types are added (<c>modelBuilder.Entity&lt;T&gt;()</c>, or the <c>DbSet</c>s).
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

        DeclareLocalizedStrings(modelBuilder);

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
                    property.SetValueComparer(new LocalizedStringValueComparer());
                }

        return modelBuilder;
    }

    /// <summary>
    ///     Declares as a property every <see cref="LocalizedString" /> member EF Core's conventions took for a navigation.
    /// </summary>
    /// <remarks>
    ///     A <c>LocalizedString</c> nobody declared is not a scalar property to EF Core: its conventions make it a navigation to an entity type
    ///     <c>LocalizedString</c>, and the model fails with "The entity type 'LocalizedString' requires a primary key to be defined". Declared
    ///     explicitly, the member is a property, the convention's navigation and entity type go, and the loop that follows gives it its
    ///     converter and comparer. The members are read from the model's own navigations, not from the CLR type.
    /// </remarks>
    private static void DeclareLocalizedStrings(ModelBuilder modelBuilder)
    {
        var discovered = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => entityType.ClrType != typeof(LocalizedString))
            .SelectMany(entityType => entityType.GetDeclaredNavigations()
                .Where(navigation => navigation.TargetEntityType.ClrType == typeof(LocalizedString))
                .Select(navigation => (EntityType: entityType.ClrType, Member: navigation.Name)))
            .ToList();

        foreach (var (entityType, member) in discovered)
            modelBuilder.Entity(entityType).Property(typeof(LocalizedString), member);
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
            .HaveConversion<LocalizedStringValueConverter, LocalizedStringValueComparer>();
    }
}