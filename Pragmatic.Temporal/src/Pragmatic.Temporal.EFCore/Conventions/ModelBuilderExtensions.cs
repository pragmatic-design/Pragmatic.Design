using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.EntityFrameworkCore.Conventions;

/// <summary>
///     Extension methods for ModelBuilder to configure Pragmatic.Temporal types.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    ///     Applies Pragmatic.Temporal conventions to the model.
    ///     Call this in OnModelCreating if not using UsePragmaticTemporal().
    ///     Produces the same configuration as <see cref="TemporalModelConvention" />.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="options">Optional EF Core temporal options (defaults: Duration as ticks).</param>
    public static ModelBuilder ApplyTemporalConventions(
        this ModelBuilder modelBuilder,
        TemporalEfCoreOptions? options = null)
    {
        options ??= new TemporalEfCoreOptions();

        ReclaimCronExpressionMembers(modelBuilder);
        AddUndiscoveredTemporalProperties(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var property in entityType.GetProperties())
                ConfigureProperty(property, options);

        return modelBuilder;
    }

    /// <summary>
    ///     EF property discovery does not recognize temporal types as primitives, so by
    ///     OnModelCreating their CLR members are simply unmapped. Add them explicitly, from the list the
    ///     generator produced: finding them is a property scan over a closed set of types the compiler
    ///     can see, so it belongs to the generator, not to reflection at model building.
    /// </summary>
    private static void AddUndiscoveredTemporalProperties(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // The generator listed these, so nothing tests the closed set of temporal types against every
            // public instance property of every entity at startup.
            foreach (var temporal in TemporalPropertyRegistry.For(entityType.ClrType))
            {
                if (entityType.FindProperty(temporal.Name) is null
                    && entityType.FindNavigation(temporal.Name) is null)
                    entityType.AddProperty(temporal.Name, temporal.ClrType);
            }
        }
    }

    /// <summary>
    ///     CronExpression is a reference type, so by the time OnModelCreating runs,
    ///     relationship discovery has already modeled it as an entity/navigation.
    ///     Capture the members, drop the spurious entity type, and re-add them as
    ///     scalar properties so the converter pass can pick them up.
    /// </summary>
    private static void ReclaimCronExpressionMembers(ModelBuilder modelBuilder)
    {
        var cronMembers = new List<(IMutableEntityType Owner, PropertyInfo Member)>();
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            foreach (var navigation in entityType.GetNavigations())
            {
                if (navigation.TargetEntityType.ClrType == typeof(CronExpression)
                    && navigation.PropertyInfo is { } member)
                    cronMembers.Add((entityType, member));
            }

        if (cronMembers.Count == 0 && modelBuilder.Model.FindEntityType(typeof(CronExpression)) is null)
            return;

        modelBuilder.Ignore<CronExpression>();

        foreach (var (owner, member) in cronMembers)
            owner.AddProperty(member);
    }

    private static void ConfigureProperty(IMutableProperty property, TemporalEfCoreOptions options)
    {
        var mapping = TemporalPropertyConfigurator.GetMapping(property.ClrType, property.IsNullable, options);
        if (mapping is not { } m)
            return;

        property.SetValueConverter(m.ConverterType);
        if (m.MaxLength is { } maxLength)
            property.SetMaxLength(maxLength);
    }
}
