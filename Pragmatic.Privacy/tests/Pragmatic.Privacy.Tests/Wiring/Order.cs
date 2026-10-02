using Pragmatic.Privacy;

namespace Pragmatic.Privacy.Tests.Wiring;

/// <summary>
///     An entity that holds the subject's data without being the subject — the case the generated
///     extractor alone cannot answer, because finding these rows starts from a subject reference.
/// </summary>
[LinksToSubject(nameof(Customer))]
public class Order
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the subject.</summary>
    public int CustomerId { get; set; }

    /// <summary>The declared path to the subject.</summary>
    public Customer Customer { get; set; } = null!;

    /// <summary>Cleared on erasure, so nullable — a NOT NULL column would reject the plan.</summary>
    [PersonalData(DataCategory.Location, Erasure = ErasureStrategy.Null)]
    public string? ShippingAddress { get; set; }
}
