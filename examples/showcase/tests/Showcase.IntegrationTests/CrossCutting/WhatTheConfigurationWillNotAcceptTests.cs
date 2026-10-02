using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration;
using Pragmatic.Testing.Assertions;
using Showcase.Host.Configuration;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     The two things an options class declares that <c>[Range]</c> cannot: a rule
///     <b>between</b> two settings, and a value the change history must not keep.
/// </summary>
public class WhatTheConfigurationWillNotAcceptTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // ── [ConfigInvariant] ────────────────────────────────────────────────────────

    /// <summary>
    ///     A booking floor above the property ceiling is refused, though both numbers are in range.
    /// </summary>
    /// <remarks>
    ///     ⚠️ 501 and 500 each satisfy their own <c>[Range(1, 500)]</c>… except 501 does not, so the
    ///     pair chosen here is 400 and 300: both valid alone, and together a configuration that
    ///     accepts no booking at all. That is what DataAnnotations cannot see and what the
    ///     generated <c>IValidateOptions</c> runs the invariant for.
    /// </remarks>
    [Fact]
    public void AFloorAboveTheCeiling_IsRefusedByTheInvariant()
    {
        var validator = Services.GetRequiredService<IValidateOptions<ShowcaseOptions>>();

        var result = validator.Validate(Options.DefaultName, new ShowcaseOptions
        {
            MaxGuestsPerProperty = 300,
            MinGuestsPerBooking = 400
        });

        result.Failed.Should().BeTrue("no booking fits between a floor of 400 and a ceiling of 300");
        string.Join(" ", result.Failures ?? []).Should().Contain("MinGuestsPerBooking cannot exceed",
            "the message on the declaration is what an operator reads at startup");
    }

    /// <summary>
    ///     The control: the same validator accepts the pair the other way round.
    /// </summary>
    /// <remarks>
    ///     Without it, "the invariant refuses" is satisfied by a validator that refuses everything —
    ///     which is what an invariant returning the wrong way round would do, and it would stop the
    ///     application from starting at all rather than only on a bad pair.
    /// </remarks>
    [Fact]
    public void AFloorUnderTheCeiling_IsAccepted()
    {
        var validator = Services.GetRequiredService<IValidateOptions<ShowcaseOptions>>();

        var result = validator.Validate(Options.DefaultName, new ShowcaseOptions
        {
            MaxGuestsPerProperty = 300,
            MinGuestsPerBooking = 2
        });

        result.Succeeded.Should().BeTrue("{0}", string.Join(" ", result.Failures ?? []));
    }

    // ── [Sensitive] ──────────────────────────────────────────────────────────────

    /// <summary>The key the audit must not keep in clear is known at compile time.</summary>
    /// <remarks>
    ///     The classifier is generated from the declaration and is what a configuration store asks
    ///     before writing a change into the trail — no reflection, and nothing to keep in step by
    ///     hand.
    /// </remarks>
    [Fact]
    public void TheApiKey_IsClassifiedSensitive()
    {
        var classifier = Services.GetRequiredService<ISensitiveKeyClassifier>();

        classifier.IsSensitive("Showcase:PaymentProviderApiKey").Should().BeTrue();
    }

    /// <summary>
    ///     The control: a setting nobody marked is not masked.
    /// </summary>
    /// <remarks>
    ///     Without it, "the key is sensitive" is satisfied by a classifier that answers true to
    ///     everything — which would mask the whole change history and read, from the outside, as a
    ///     working one.
    /// </remarks>
    [Fact]
    public void ASettingNobodyMarked_IsNot()
    {
        var classifier = Services.GetRequiredService<ISensitiveKeyClassifier>();

        classifier.IsSensitive("Showcase:AppName").Should().BeFalse();
        classifier.IsSensitive("Showcase:MaxGuestsPerProperty").Should().BeFalse();
    }
}
