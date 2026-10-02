using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.Conventions;

/// <summary>
///     Model convention that automatically configures Pragmatic.Temporal types.
///     Added automatically by <c>UsePragmaticTemporal()</c>; can also be registered
///     manually in <c>ConfigureConventions</c>.
/// </summary>
/// <remarks>
///     CronExpression is a reference type: without intervention EF's relationship
///     discovery models it as an entity/navigation (and later fails constructor
///     binding). This convention therefore hooks three stages: model initialized
///     (ignore CronExpression as an entity type), entity type added (map CronExpression
///     members as scalar properties), and model finalizing (assign converters).
/// </remarks>
public sealed class TemporalModelConvention
    : IModelInitializedConvention, IEntityTypeAddedConvention, IModelFinalizingConvention
{
    private readonly TemporalEfCoreOptions _options;

    /// <summary>Creates a convention with default options (Duration stored as ticks).</summary>
    public TemporalModelConvention()
        : this(new TemporalEfCoreOptions())
    {
    }

    /// <summary>Creates a convention honoring the given options.</summary>
    public TemporalModelConvention(TemporalEfCoreOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public void ProcessModelInitialized(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        // Never let relationship discovery model CronExpression as an entity type.
        modelBuilder.Ignore(typeof(CronExpression));
    }

    /// <inheritdoc />
    public void ProcessEntityTypeAdded(
        IConventionEntityTypeBuilder entityTypeBuilder,
        IConventionContext<IConventionEntityTypeBuilder> context)
    {
        // EF property discovery does not recognize any temporal type as a primitive, so map the members
        // explicitly — from the generated list rather than by scanning every public instance property
        // and testing its type against the eight temporal ones.
        foreach (var temporal in TemporalPropertyRegistry.For(entityTypeBuilder.Metadata.ClrType))
            entityTypeBuilder.Property(temporal.ClrType, temporal.Name);
    }

    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                ConfigureProperty(property);
    }

    private void ConfigureProperty(IConventionProperty property)
    {
        if (ConfigureInstant(property))
            return;

        var mapping = TemporalPropertyConfigurator.GetMapping(property.ClrType, property.IsNullable, _options);
        if (mapping is not { } m)
            return;

        property.SetValueConverter(m.ConverterType);
        if (m.MaxLength is { } maxLength)
            property.SetMaxLength(maxLength);
    }

    /// <summary>
    ///     Makes «storage is UTC» true for the two BCL instant types.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the property was an instant, whether or not it was converted:
    ///     neither type is one of the module's own, so there is nothing after this to try.
    /// </returns>
    /// <remarks>
    ///     The rest of the framework reads a stored instant as UTC — the JSON layer converts outward
    ///     from that assumption. This is what makes it so: without it, a value written with an offset
    ///     reaches the column as it is, and every later conversion works from a number whose meaning
    ///     depends on who wrote it. No exception, no diagnostic, a payload wrong by an offset.
    /// </remarks>
    private bool ConfigureInstant(IConventionProperty property)
    {
        var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        if (clrType != typeof(DateTimeOffset) && clrType != typeof(DateTime))
            return false;

        if (!_options.NormalizeInstantsToUtc)
            return true;

        // ⚠️ A converter already on the property is somebody's explicit decision, and overwriting it
        // changes the column's type from under them. The audit log stores its instants as bigint ticks;
        // replacing that converter sent a timestamptz at a bigint column and every write in the
        // application failed — the error names a column, not this convention.
        if (property.GetValueConverter() is not null || property.GetProviderClrType() is not null)
            return true;

        property.SetValueConverter(clrType == typeof(DateTimeOffset)
            ? typeof(ValueConverters.UtcDateTimeOffsetConverter)
            : typeof(ValueConverters.UtcDateTimeConverter));

        return true;
    }
}
