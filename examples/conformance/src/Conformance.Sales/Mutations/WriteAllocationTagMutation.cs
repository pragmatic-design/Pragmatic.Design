using Pragmatic.Actions.Mutation;
using Pragmatic.Validation.Attributes;
using Conformance.Sales.Entities;

namespace Conformance.Sales.Mutations;

/// <summary>The third level, as a mutation child.</summary>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteAllocationTagMutation : Mutation<AllocationTag>
{
    public Guid Id { get; init; }

    /// <summary>
    ///     ⚠️ The rule that measures how far nested validation reaches.
    /// </summary>
    /// <remarks>
    ///     This is the <b>third</b> level of the chain, and the two above it have no rules of their own —
    ///     so they get no validator, and their loop over the elements is not emitted. If this rule were
    ///     not applied, it would be because the chain breaks where an intermediate validator is missing.
    ///     It is the case of <c>TheRuleThreeLevelsDown</c>.
    /// </remarks>
    [NotEmpty]
    public string Label { get; init; } = "";

    /// <summary>The fourth level.</summary>
    public List<WriteTagNoteMutation> Notes { get; init; } = [];
}
