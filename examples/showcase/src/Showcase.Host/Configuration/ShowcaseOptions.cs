using System.ComponentModel.DataAnnotations;

namespace Showcase.Host.Configuration;

/// <summary>
/// Application-level configuration for the Showcase app.
/// Demonstrates [Configuration] attribute: the source generator automatically generates
/// IServiceCollection binding, DataAnnotation validation, and ValidateOnStart registration.
/// </summary>
[Configuration]
public partial class ShowcaseOptions
{
    /// <summary>Display name for the application.</summary>
    [MaxLength(100)]
    public string AppName { get; set; } = "Pragmatic Showcase";

    /// <summary>Maximum number of guests per property.</summary>
    [Range(1, 500)]
    public int MaxGuestsPerProperty { get; set; } = 100;

    /// <summary>Smallest booking the property accepts.</summary>
    [Range(1, 500)]
    public int MinGuestsPerBooking { get; set; } = 1;

    /// <summary>
    ///     The credential the configured payment provider is called with.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>[Sensitive]</c> is about the <b>change history</b>, not about encryption: the
    ///     generator records the key at compile time so a configuration store writes
    ///     <c>"(sensitive)"</c> into the audit instead of the value. A key that is rotated through
    ///     the configuration UI would otherwise be readable for ever in the trail of who changed
    ///     what, which is the one place nobody thinks to look.
    /// </remarks>
    [Sensitive]
    public string PaymentProviderApiKey { get; set; } = "";

    /// <summary>Default cancellation window in hours before check-in.</summary>
    [Range(1, 168)]
    public int CancellationWindowHours { get; set; } = 24;

    /// <summary>Whether to enable demo data seeding on startup.</summary>
    public bool SeedDemoData { get; set; } = true;

    /// <summary>
    ///     The rule neither <c>[Range]</c> can state: a booking floor above the property ceiling
    ///     accepts nothing at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Both properties are individually valid — 500 and 501 are inside their ranges — so
    ///     DataAnnotations pass and the application starts with a configuration that refuses every
    ///     booking. <c>[ConfigInvariant]</c> is where a rule <em>between</em> two settings is
    ///     declared, and the generated <c>IValidateOptions</c> runs it at startup, so the failure
    ///     is a refusal to start rather than an empty search at the front desk.
    /// </remarks>
    [ConfigInvariant("MinGuestsPerBooking cannot exceed MaxGuestsPerProperty: no booking would fit.")]
    public bool TheBookingFloorFitsUnderTheCeiling() => MinGuestsPerBooking <= MaxGuestsPerProperty;
}
