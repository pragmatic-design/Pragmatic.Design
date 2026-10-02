namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Specifies the inheritance mapping strategy for an entity hierarchy.
///     Apply to the base entity class in the hierarchy.
/// </summary>
/// <remarks>
///     Usage:
///     <code>
/// [Entity]
/// [Inheritance(InheritanceStrategy.TPH, DiscriminatorColumn = "Type")]
/// public abstract partial class BaseEntity { }
///
/// [Entity]
/// public partial class DerivedEntity : BaseEntity { }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InheritanceAttribute : Attribute
{
    /// <summary>
    ///     Creates an inheritance attribute with the specified strategy.
    /// </summary>
    /// <param name="strategy">The inheritance mapping strategy.</param>
    public InheritanceAttribute(InheritanceStrategy strategy)
    {
        Strategy = strategy;
    }

    /// <summary>
    ///     Gets the inheritance strategy.
    /// </summary>
    public InheritanceStrategy Strategy { get; }

    /// <summary>
    ///     Gets or sets the discriminator column name for TPH strategy.
    ///     Defaults to "Discriminator".
    /// </summary>
    public string DiscriminatorColumn { get; set; } = "Discriminator";

    /// <summary>
    ///     Gets or sets the discriminator value for this entity type.
    ///     Defaults to the class name.
    /// </summary>
    public string? DiscriminatorValue { get; set; }
}
