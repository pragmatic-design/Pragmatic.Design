using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     Four scalars, each of a type an HTTP request does not naturally carry.
/// </summary>
/// <remarks>
///     <para>
///         It exists for a single cell: <em>automatic conversion applies to writes too</em>. Numeric to
///         string, <c>Guid</c> and <c>bool</c> each get a case on that path, executed rather than
///         asserted on the generated text.
///     </para>
///     <para>
///         The four properties have no domain meaning, deliberately: it is a table of conversions, not a
///         piece of the sales model. Putting them on <c>Order</c> would have mixed the cell with the many
///         that entity already serves.
///     </para>
/// </remarks>
[Entity]
public partial class ConversionSubject : IEntity
{
    /// <summary>The <c>string</c> → <c>Guid</c> direction.</summary>
    public Guid ExternalId { get; private set; }

    /// <summary>The <c>string</c> → <c>bool</c> direction.</summary>
    public bool IsPriority { get; private set; }

    /// <summary>The <c>string</c> → numeric direction.</summary>
    public int Quantity { get; private set; }

    /// <summary>And the opposite direction: numeric → <c>string</c>.</summary>
    [Required]
    public string Code { get; private set; } = "";
}
