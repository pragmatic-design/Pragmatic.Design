using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Compositions.Models;

/// <summary>
///     Contribution for entity properties with [ComputedDefault] attributes.
///     Contains the list of properties that need default values generated at creation time.
/// </summary>
internal sealed record ComputedDefaultContribution
{
    /// <summary>Properties that require computed default value generation. EquatableArray (not raw
    /// ImmutableArray) keeps this model value-equatable so it doesn't break incremental caching when
    /// embedded in MutationModel.</summary>
    public required EquatableArray<ComputedDefaultPropertyModel> Properties { get; init; }
}

/// <summary>
///     A single property with a [ComputedDefault] attribute.
/// </summary>
internal sealed record ComputedDefaultPropertyModel
{
    /// <summary>The property name on the entity (e.g., "InvoiceNumber").</summary>
    public required string PropertyName { get; init; }

    /// <summary>The setter method name (e.g., "SetInvoiceNumber" for private setter).</summary>
    public required string SetterName { get; init; }

    /// <summary>The value type FQN (e.g., "string", "int").</summary>
    public required string ValueTypeFqn { get; init; }

    /// <summary>The generator type FQN (e.g., "global::MyApp.InvoiceNumberGenerator").</summary>
    public required string GeneratorTypeFqn { get; init; }

    /// <summary>The entity type FQN (e.g., "global::MyApp.Invoice").</summary>
    public required string EntityTypeFqn { get; init; }

    /// <summary>
    ///     Whether the generator depends on scoped services (a keyed DbContext) and must therefore be
    ///     registered as scoped rather than singleton. True for sequence-backed [GeneratedValue] {SEQ:N}
    ///     formatters (they do a DB round-trip); false for stateless [ComputedDefault] generators.
    /// </summary>
    public bool RequiresScope { get; init; }
}
