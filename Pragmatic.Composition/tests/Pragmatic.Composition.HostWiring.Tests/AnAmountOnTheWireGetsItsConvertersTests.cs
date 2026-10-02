// Pragmatic.Composition.HostWiring.Tests - An amount on the wire is a declaration too

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     An application that puts a <c>Money</c> on the wire gets the JSON converters for it, even when it
///     has no translation file and calls no <c>UseI18N</c>.
/// </summary>
/// <remarks>
///     <para>
///         The converters are installed by <c>AddPragmaticInternationalization</c>, which the generated
///         host calls only when i18n was <b>declared</b> — a translation file. That gate is the rule
///         <see cref="ACapabilityNobodyDeclaredIsNotWiredTests" /> defends, and it is right. What was
///         missing is that an amount on the wire is a declaration too, and the generator can see it.
///     </para>
///     <para>
///         ⚠️ Without them every request carrying a <c>Money</c> is a <b>400 before any rule runs</b>, and
///         the application starts perfectly: with no translations the host registers no i18n at all, so it
///         never reaches the <c>I18NConfigurationException</c> that would otherwise refuse the start
///         (measured in the Invoicing example before it had any translations).
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class AnAmountOnTheWireGetsItsConvertersTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    /// <summary>The converters alone, without the rest of the i18n services.</summary>
    private const string Converters =
        "services.ConfigureHttpJsonOptions(json => json.SerializerOptions.AddPragmaticInternationalization());";

    /// <summary>The full registration, which a translated application gets instead.</summary>
    private const string FullI18n = "services.AddPragmaticInternationalization(configuration);";

    /// <summary>A module that answers with an amount, and is translated into nothing.</summary>
    private const string AnEndpointThatAnswersMoney = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Composition.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Internationalization.Types;
        using Pragmatic.Result;

        namespace Probe.Prices;

        public sealed class PriceDto
        {
            public Money Amount { get; init; }
        }

        [Endpoint(HttpVerb.Get, "api/price")]
        public partial class GetPriceEndpoint : Endpoint<PriceDto>
        {
            public override Task<Result<PriceDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<PriceDto>.Success(new PriceDto()));
        }
        """;

    [Fact]
    public void AMoneyOnTheWire_WithNoTranslations_GetsTheConverters()
    {
        var lines = HostWiringFixture.LinesOf(HostWiringFixture.GeneratedHostOf("Probe.Money", AnEndpointThatAnswersMoney), HostServices);

        lines.Should().Contain(Converters,
            "the amount is the declaration: without the converters every request carrying one is a 400, "
            + $"and the application starts. Host.Services.g.cs has {lines.Count} lines");
        lines.Should().NotContain(FullI18n,
            "nothing here asks for cultures, resources or a request culture provider — only for the "
            + "shape of an amount on the wire");
    }

    /// <summary>
    ///     ⚠️ The control that keeps this from becoming "register i18n whenever the package is
    ///     referenced": a host that declares nothing gets neither line.
    /// </summary>
    [Fact]
    public void AHostThatDeclaresNothing_GetsNeitherLine()
    {
        var text = string.Join("\n", HostWiringFixture.LinesOf(fixture.Bare, HostServices));

        text.Should().NotContain("AddPragmaticInternationalization");
    }

    /// <summary>
    ///     The second control: a translated application gets the full registration, which installs the
    ///     converters itself — so the narrow line would be a second registration of the same converters.
    /// </summary>
    [Fact]
    public void ATranslatedApplication_GetsTheFullRegistrationAndNotTheNarrowOne()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(FullI18n);
        lines.Should().NotContain(Converters);
    }
}
