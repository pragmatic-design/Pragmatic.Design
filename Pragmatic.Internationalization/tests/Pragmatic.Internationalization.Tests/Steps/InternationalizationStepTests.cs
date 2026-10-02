using System;
using System.Linq;
using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Steps;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.AspNetCore.Steps;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.Steps;

public class InternationalizationStepTests
{
    /// <summary>
    ///     After authentication, before the endpoint. Before routing would be too early: the culture
    ///     would be resolved before anyone knew who was asking, so a per-user culture provider would
    ///     always see an anonymous request.
    /// </summary>
    [Fact]
    public void Order_RunsAfterAuthentication()
    {
        new InternationalizationStep().Order.Should().BeGreaterThan(
            new AuthenticationStep().Order,
            "a provider that reads the user's stored language needs an authenticated caller");
    }

    /// <summary>
    ///     And before authorization, because the 403 that middleware writes is the only error in an
    ///     application not written by an endpoint — so it is the only one that can be written before a
    ///     culture exists for the request.
    /// </summary>
    /// <remarks>
    ///     With the culture resolved after authorization, the refusal's title would come from
    ///     <c>CultureInfo.CurrentCulture</c> — the machine's locale — and the same request would answer
    ///     in Italian on a developer's machine and in English on the CI runner. The ordering is asserted
    ///     through the key the generated host sorts by, not by comparing two constants: that key is
    ///     <c>OrderBy(Order).ThenBy(type name)</c>, and the tiebreak is part of the answer.
    /// </remarks>
    [Fact]
    public void Order_ResolvesTheCulture_BeforeAuthorizationCanRefuse()
    {
        IStartupStep[] steps = [new AuthorizationStep(), new InternationalizationStep(), new AuthenticationStep()];

        var pipeline = steps
            .OrderBy(s => s.Order)
            .ThenBy(s => s.GetType().FullName, StringComparer.Ordinal)
            .Select(s => s.GetType().Name)
            .ToArray();

        pipeline.Should().Equal(
            nameof(AuthenticationStep),
            nameof(InternationalizationStep),
            nameof(AuthorizationStep));
    }

    [Fact]
    public void ConfigurePipeline_RegistersMiddleware_WithoutThrowing()
    {
        var app = new ApplicationBuilder(ConfiguredServices());
        var step = new InternationalizationStep();

        var act = () => step.ConfigurePipeline(app);

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigurePipeline_ProducesRequestDelegate()
    {
        var app = new ApplicationBuilder(ConfiguredServices());
        var step = new InternationalizationStep();

        step.ConfigurePipeline(app);

        app.Build().Should().NotBeNull();
    }

    /// <summary>
    ///     Without a culture from anywhere the pipeline is not built: the host does not start, rather
    ///     than start and fail every request with the same exception.
    /// </summary>
    [Fact]
    public void ConfigurePipeline_WithoutACulture_RefusesToStart()
    {
        var services = new ServiceCollection();
        services.AddPragmaticInternationalization();
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => new InternationalizationStep().ConfigurePipeline(app);

        act.Should().Throw<I18NConfigurationException>();
    }

    private static ServiceProvider ConfiguredServices()
    {
        var services = new ServiceCollection();
        services.AddPragmaticInternationalization(options => options.DefaultUICulture = CultureCode.EnglishUS);
        return services.BuildServiceProvider();
    }
}
