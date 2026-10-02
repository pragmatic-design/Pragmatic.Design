using Pragmatic.MultiTenancy;

namespace Invoicing.Registry.Entities;

/// <summary>
///     Somebody a company bills: who they are, where the invoice goes, in which language, and how long
///     they have to pay.
/// </summary>
/// <remarks>
///     <para>
///         The VAT number is unique <b>within the company</b> and not beyond it — <c>[Unique]</c>'s
///         default scope, and the right one here: two of our customers may legitimately bill the same
///         firm, and only inside one of them would a repeat be a mistake.
///     </para>
///     <para>
///         Soft-deleted rather than removed, declared now although nothing deletes a customer yet: adding
///         it later rewrites the schema and every read, and an invoice outlives the relationship it was
///         issued for.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Audited]
[SoftDelete]
[ConcurrencyAware]
[Unique(nameof(VatNumber))]
public partial class Customer : IEntity, ITenantEntity
{
    /// <summary>The company this customer belongs to; the interceptor writes it.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>How the accountant refers to them — <c>CUS-00001</c>, numbered by a database sequence.</summary>
    /// <remarks>
    ///     A sequence is global to the table, so the numbers of one company have holes where another
    ///     company's customers fell. That is fine for a customer code, and it is exactly why an invoice
    ///     number cannot be built this way.
    /// </remarks>
    [LogicKey]
    [GeneratedValue("CUS-{SEQ:5}")]
    public string Code { get; private set; } = "";

    [Required]
    [MaxLength(200)]
    public string Name { get; private set; } = "";

    [MaxLength(20)]
    public string VatNumber { get; private set; } = "";

    [Required]
    [Email]
    [MaxLength(320)]
    public string Email { get; private set; } = "";

    public PostalAddress Address { get; private set; } = new("", "", "", "");

    /// <summary>The language their invoices and reminders are written in.</summary>
    [Required]
    [MaxLength(10)]
    [OneOf("en-US", "it-IT")]
    public string PreferredCulture { get; private set; } = "en-US";

    /// <summary>Days from the issue date to the due date.</summary>
    [Range(0, 180)]
    public int PaymentTermsDays { get; private set; } = 30;
}
