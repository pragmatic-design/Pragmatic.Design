using Pragmatic.Specification;

namespace Conformance.Sales.Entities;

/// <summary>
///     The rules a label is read by — the other half of the generated <c>LabelSpecifications</c>.
/// </summary>
/// <remarks>
///     They exist for the loads by rule (<c>[LoadEntity(Specification = …)]</c>): their parameters bind by
///     name to the properties of the operations that read by them.
/// </remarks>
public static partial class LabelSpecifications
{
    /// <summary>The label called <paramref name="name" />.</summary>
    public static Specification<Label> Named(string name) => Spec<Label>.Where(l => l.Name == name);

    /// <summary>The labels whose name starts with <paramref name="prefix" />.</summary>
    public static Specification<Label> NamedStartingWith(string prefix)
        => Spec<Label>.Where(l => l.Name.StartsWith(prefix));
}
