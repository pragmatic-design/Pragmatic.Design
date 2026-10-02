namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as having preset children that should be created automatically.
///     Use together with <see cref="PresetProviderAttribute{TProvider}" /> to specify providers.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HasPresetsAttribute : Attribute;
