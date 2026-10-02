using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     A shape that looks like personal data does not survive into this application's audit trail,
///     whatever a caller puts in an entry's free-text detail.
/// </summary>
/// <remarks>
///     <para>
///         The trail and the logging pipeline share one pattern set (<c>Pragmatic.Redaction</c>). A trail
///         that let a national identifier or a card-verification value through in plaintext while the
///         logs caught both would be the wrong way round, since the trail is append-only and kept for
///         years. <c>PatternAuditDetailRedactor</c> is registered by <c>AddAudit</c> itself, so an
///         application gets the floor without asking for it.
///     </para>
///     <para>
///         ⚠️ <b>This is a floor, not a guarantee</b>, and the module's own words say so: pattern
///         matching cannot recognise a name, an address, or a sentence about someone's health. What it
///         promises is that the obvious shapes do not get through, and that is what is asserted here.
///     </para>
///     <para>
///         ⚠️ <b>It writes the entry itself, and that is deliberate.</b> Time off puts no user-typed
///         text into a detail — <c>RecordTheDecision</c> records references and nothing else, and says
///         why — so there is no domain path to drive this through, and inventing a feature to carry a
///         mechanism is the thing this example refuses to do. What is proved is the guarantee the host
///         configures, through the application's own trail and its own reader, which is the same shape
///         as <see cref="WhatTheLogsMayNotSay" /> and stated there too.
///     </para>
///     <para>
///         Declared redaction — the classified members of an entity, masked before a log provider sees
///         them — is <see cref="WhatTheLogsMayNotSay" />. That one catches what was <em>declared</em>;
///         this one catches what a shape <em>looks like</em>, and neither replaces the other.
///     </para>
/// </remarks>
public sealed class WhatTheTrailMayNotKeep(PostgresFixture database) : TimeOffTestBase(database)
{
    [Fact]
    public async Task ANationalIdentifier_DoesNotSurviveIntoTheTrail()
    {
        var detail = await RecordedDetailAsync("submitted with tax code 123-45-6789 attached");

        detail.Should().NotContain("123-45-6789");
        detail.Should().Contain("[redacted]");
    }

    /// <summary>
    ///     A card verification value, which PCI DSS forbids storing at all — so letting one through is
    ///     worse than most leaks.
    /// </summary>
    /// <remarks>
    ///     Three or four digits are unrecognisable on their own; the label is the only signal, which is
    ///     why the pattern is written around it.
    /// </remarks>
    [Fact]
    public async Task ACardVerificationValue_DoesNotSurviveIntoTheTrail()
    {
        var detail = await RecordedDetailAsync("refund refused, cvv: 4127 did not match");

        detail.Should().NotContain("4127");
        detail.Should().Contain("[redacted]");
    }

    /// <summary>
    ///     The control: a detail that carries no such shape is kept exactly as it was written.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, "the value is gone" is satisfied by a trail that drops details, by a redactor
    ///     that masks everything, and by a read that returns nothing — and each of those would read as
    ///     a working floor while destroying the one field that explains why an entry exists.
    ///     <para>
    ///         The date is the case the module names as deliberately absent: a birth date is personal
    ///         data and "locked until 2026-08-01" is not, and a pattern cannot tell them apart.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task WhatCarriesNoSuchShape_IsKeptAsItWasWritten()
    {
        const string plain = "locked until 2026-08-01, after five attempts";

        (await RecordedDetailAsync(plain)).Should().Be(plain);
    }

    /// <summary>
    ///     Writes one entry through the application's own trail and reads back what was kept.
    /// </summary>
    /// <remarks>
    ///     Through <c>IAuditTrail</c> and <c>IAuditTrailReader</c> — the two the host registered — so
    ///     what is measured is this deployment's configuration and not a redactor constructed here.
    /// </remarks>
    private async Task<string?> RecordedDetailAsync(string detail)
    {
        // A key of its own, and read back by it: this class shares the suite's database, so a filter
        // on the category or a page of the newest entries would be counting the suite rather than
        // this entry.
        var id = $"{Guid.NewGuid():N}";

        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAuditTrail>().RecordAsync(new AuditEntry
        {
            SegmentId = string.Empty,
            OccurredAt = DateTimeOffset.UtcNow,
            Category = AuditCategory.Security,
            Operation = "Security.DetailRedaction",
            TargetType = nameof(WhatTheTrailMayNotKeep),
            TargetId = id,
            Outcome = AuditOutcome.Failed,
            Detail = detail
        });

        var page = await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = nameof(WhatTheTrailMayNotKeep), TargetId = id });

        return page.Entries.Should().ContainSingle().Which.Detail;
    }
}
