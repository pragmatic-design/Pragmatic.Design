namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A property with <c>[ComputedDefault&lt;TEntity, TValue, TGenerator&gt;]</c>: the generator and the binding the unit of work applies at
///     the save, on every engine, to an added entity whose property is still empty (#740).
/// </summary>
internal sealed record ComputedDefaultModel
{
    /// <summary>Fully-qualified entity type, with the <c>global::</c> prefix.</summary>
    public required string EntityFqn { get; init; }

    /// <summary>The entity's namespace, empty for the global one.</summary>
    public required string Namespace { get; init; }

    /// <summary>The property the generator fills.</summary>
    public required string PropertyName { get; init; }

    /// <summary>The property itself when its setter is public, otherwise the generated <c>Set{Property}</c>.</summary>
    public required string SetterName { get; init; }

    /// <summary>The property's type, fully qualified.</summary>
    public required string ValueTypeFqn { get; init; }

    /// <summary>The generator type, fully qualified.</summary>
    public required string GeneratorTypeFqn { get; init; }

    /// <summary>Whether the value is a string, empty when null or "": any other type is empty when it is its default.</summary>
    public bool IsString { get; init; }
}
