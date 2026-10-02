namespace Casework.Intake.Entities;

/// <summary>
///     The address an organisation's mail goes out from, and the name it signs with.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>There is no default here, and that is the point.</b> A default sender would send every
///         organisation's mail from one address — the application's — and an applicant who replied would
///         be writing to the wrong people about somebody else's case. An organisation that has not given
///         an address has no mail: the handler says so and stops, which is the same choice Invoicing made
///         when it left <c>DefaultFrom</c> unset.
///     </para>
///     <para>
///         One row per organisation, and an <c>ITenantEntity</c>: its address is in its own database or
///         behind its own tenant column, and no path reads one organisation's while serving another.
///     </para>
///     <para>
///         ⚠️ It is <b>not</b> in the tenant register. The register's row is a <c>TenantInfo</c>, a
///         framework type this application cannot add a field to — and the register is read before
///         anybody knows which tenant the request belongs to, which is the wrong moment to be carrying an
///         organisation's correspondence details anyway.
///     </para>
/// </remarks>
[Entity]
[Audited]
[Auditable]
public partial class CorrespondenceSettings : IEntity, ITenantEntity
{
    public string TenantId { get; set; } = "";

    /// <summary>Where the organisation's mail comes from — and where a reply goes.</summary>
    [Required]
    [MaxLength(200)]
    public string SenderAddress { get; private set; } = "";

    /// <summary>How it signs: the name an applicant sees beside the address.</summary>
    [Required]
    [MaxLength(200)]
    public string SenderName { get; private set; } = "";

    internal static CorrespondenceSettings Of(string address, string name)
    {
        var settings = Create();
        settings.SetSenderAddress(address);
        settings.SetSenderName(name);

        return settings;
    }

    internal void ChangeTo(string address, string name)
    {
        SetSenderAddress(address);
        SetSenderName(name);
    }
}
