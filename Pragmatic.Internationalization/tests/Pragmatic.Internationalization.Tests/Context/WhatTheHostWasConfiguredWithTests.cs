using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

/// <summary>
///     <see cref="IConfiguredCultures" /> answers the one question a module asks of the
///     configuration: "what language when nobody said?".
/// </summary>
/// <remarks>
///     <para>
///         The answer is the host's <c>i18n.DefaultCulture(...)</c>, merged across providers by
///         <see cref="I18NConfigResolver" /> — the same resolution the request pipeline gets, not a
///         second reading of the options.
///     </para>
///     <para>
///         ⚠️ The wrong answers a module can reach for are in the contract's own remarks; the worst is
///         <c>I18NContext.Current.Culture</c>, which outside a scope is whatever the last piece of work
///         on that thread left behind.
///     </para>
/// </remarks>
public class WhatTheHostWasConfiguredWithTests
{
    private sealed class Provider(I18NConfig config, int priority = 0) : II18NConfigProvider
    {
        public int Priority => priority;

        public I18NConfig? GetConfiguration() => config;
    }

    private static IConfiguredCultures ConfiguredWith(params II18NConfigProvider[] providers)
        => new ConfiguredCultures(new I18NConfigResolver(providers));

    [Fact]
    public void TheDefault_IsTheConfiguredUICulture()
    {
        var cultures = ConfiguredWith(new Provider(new I18NConfig
        {
            DefaultUICulture = CultureCode.Italian
        }));

        cultures.Default.Should().Be(CultureCode.Italian);
    }

    /// <summary>
    ///     The merge is the resolver's, not a second reading of the options: a higher-priority provider
    ///     wins here exactly as it does for the request pipeline.
    /// </summary>
    /// <remarks>
    ///     This is the case that makes the contract <b>scoped</b>. A provider may be per-request — a
    ///     tenant's own default is the obvious one — so the answer belongs to the scope that asks, and a
    ///     singleton holding it would answer the first scope's for all of them (<c>PRAG1642</c>).
    /// </remarks>
    [Fact]
    public void AHigherPriorityProvider_IsTheOneThatAnswers()
    {
        var cultures = ConfiguredWith(
            new Provider(new I18NConfig { DefaultUICulture = CultureCode.EnglishUS }),
            new Provider(new I18NConfig { DefaultUICulture = CultureCode.Italian }, priority: 100));

        cultures.Default.Should().Be(CultureCode.Italian,
            "the tenant's configuration overrides the application's, as it does on a request");
    }

    [Fact]
    public void TheSupportedOnes_AreWhatTheHostDeclared()
    {
        var cultures = ConfiguredWith(new Provider(new I18NConfig
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, CultureCode.Italian]
        }));

        cultures.Supported.Should().HaveCount(2);
        cultures.Supported.Should().Contain(CultureCode.Italian);
    }

    /// <summary>A host that restricts nothing supports everything, and says so with an empty list.</summary>
    [Fact]
    public void WithNoRestriction_TheSupportedOnesAreEmpty()
    {
        var cultures = ConfiguredWith(new Provider(new I18NConfig
        {
            DefaultUICulture = CultureCode.EnglishUS
        }));

        cultures.Supported.Should().BeEmpty();
    }

    /// <summary>
    ///     The control: reading it cannot invent a culture. A host with none configured refuses to
    ///     start, and this refuses to answer.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without this, "answers the configured default" would be satisfied by a contract that
    ///     falls back to <c>CultureInfo.CurrentCulture</c> — which is the defect that made a module
    ///     write a constant in the first place, moved behind a nicer name.
    /// </remarks>
    [Fact]
    public void WithNothingConfigured_ItRefusesInsteadOfGuessing()
    {
        var cultures = ConfiguredWith(new Provider(new I18NConfig()));

        Assert.Throws<I18NConfigurationException>(() => cultures.Default);
    }
}
