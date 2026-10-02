using Pragmatic.Persistence.Entity;

namespace Conformance.Sales.Entities;

/// <summary>
///     A value object inside a written child.
/// </summary>
/// <remarks>
///     <para>
///         Persisted as an EF complex type: two columns, <c>Slot_Aisle</c> and <c>Slot_Shelf</c>, not a
///         separate row.
///     </para>
///     <para>
///         ⚠️ It sits on <c>Allocation</c>, which is the <b>child of a child</b>, and not on the
///         aggregate: that is the case worth proving, because a value object written only at the root
///         leaves the nested path without a consumer.
///     </para>
///     <para>
///         <c>init</c> and not read-only: EF needs to bind the constructor's parameters to the columns.
///         The same shape as <c>ContactInfo</c> in the Showcase.
///     </para>
/// </remarks>
[ValueObject]
public partial record StorageSlot
{
    public StorageSlot(string aisle, int shelf)
    {
        Aisle = aisle;
        Shelf = shelf;
    }

    public string Aisle { get; init; } = "";

    public int Shelf { get; init; }

    /// <summary>
    ///     The point where the generator hooks <c>Create</c> — without it, <c>PRAG2701</c>.
    /// </summary>
    /// <remarks>
    ///     It validates nothing of its own here: the case is about the nested write, not invariants. The
    ///     method exists because the generator requires it, and it is where the rule would go if the
    ///     type had one.
    /// </remarks>
    private static StorageSlot Validate(string aisle, int shelf) => new(aisle, shelf);
}
