using Pragmatic.Actions.Mutation;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>The fourth level: the leaf, as a mutation child.</summary>
/// <remarks>
///     ⚠️ No validation attribute, on purpose: only C#'s <c>required</c>. It is the shape for which a
///     <c>Validate()</c> is generated — the presence guard — but which no attribute declares, so a
///     <c>ValidateNestedTree()</c> that decided whether to call it by looking at the attributes, or a
///     parent loop decided the same way, would skip it. It is the case of
///     <c>TheChildThatValidatesForRequiredAlone</c>.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteTagNoteMutation : Mutation<TagNote>
{
    public Guid Id { get; init; }

    public required string Text { get; init; }
}
