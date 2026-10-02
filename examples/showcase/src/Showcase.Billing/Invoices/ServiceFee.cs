namespace Showcase.Billing.Entities;

/// <summary>
/// A service fee (e.g. cleaning, minibar, room service).
/// Derived type in TPH hierarchy under <see cref="Fee"/>.
/// </summary>
/// <remarks>
///     Carries <c>[Entity]</c>, which is the form <c>[Inheritance]</c>'s own documentation shows and
///     the form a domain needs when the derived type has to have a repository, mutations and
///     permissions of its own. A generated configuration that claims a table and a key for a derived
///     type builds clean and kills the boundary at first use; the derived types here are the
///     executable case for that.
/// </remarks>
[Entity]
public partial class ServiceFee : Fee
{
    /// <summary>The service that generated this fee.</summary>
    public string ServiceName { get; private set; } = "";

    /// <summary>Date when the service was consumed.</summary>
    public DateTimeOffset ServiceDate { get; private set; }
}
