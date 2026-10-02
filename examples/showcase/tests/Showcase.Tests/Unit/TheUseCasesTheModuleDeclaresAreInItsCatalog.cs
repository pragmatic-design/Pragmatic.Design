using System.Linq;
using Pragmatic.Testing.Assertions;
using Showcase.Booking.Generated;
using Xunit;

namespace Showcase.Tests.Unit;

/// <summary>
///     The use-case catalog an application gets for free by annotating its operations.
/// </summary>
/// <remarks>
///     <para>
///         <c>[UseCase]</c> and <c>[Rule]</c> are public attributes whose consumer is the generated
///         use-case catalog. An attribute that no application writes can be read by nobody without
///         anyone noticing, so the example writes both.
///     </para>
///     <para>
///         The generator's own suite proves the catalog is emitted from source; this proves an
///         application gets one — from two mutations in <c>Showcase.Booking</c> that enforce these
///         rules.
///     </para>
/// </remarks>
public class TheUseCasesTheModuleDeclaresAreInItsCatalog
{
    [Fact]
    public void TheCancellationUseCase_CarriesItsRulesAndWhereItLives()
    {
        var cancel = PragmaticUseCases.All.SingleOrDefault(u => u.Id == "BKG-CANCEL");

        cancel.Should().NotBeNull("Showcase.Booking declares [UseCase(\"BKG-CANCEL\")]");
        cancel!.Title.Should().Be("Cancel a reservation");
        cancel.Target.Should().Be("Showcase.Booking.Reservations.Mutations.CancelReservationMutation");
        cancel.Rules.Should().HaveCount(2);
        cancel.Rules.Should().Contain(
            "A reservation can be cancelled only before its tenant's cancellation window closes");

        cancel.File.Should().EndWith("CancelReservationMutation.cs");
        cancel.File.Should().NotContain(":",
            "an absolute build path would ship the machine that compiled this assembly");
        cancel.Line.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BothDeclaredUseCases_AreThere_AndNothingElseIs()
    {
        PragmaticUseCases.All.Select(u => u.Id).Should().BeEquivalentTo(["BKG-CANCEL", "BKG-CONFIRM"]);
    }

    /// <summary>
    ///     The control: the catalog is built from what the author wrote, not from every operation the
    ///     module has.
    /// </summary>
    /// <remarks>
    ///     Showcase.Booking has a dozen mutations, actions and queries. If the catalog listed them all
    ///     it would be a list of types with the word "use case" on it, and the two assertions above
    ///     would pass just the same — which is the shape this epic keeps finding.
    /// </remarks>
    [Fact]
    public void AnOperationThatDeclaresNothing_IsNotInTheCatalog()
    {
        PragmaticUseCases.All.Should().NotContain(
            u => u.Target.EndsWith("CheckInGuestMutation", System.StringComparison.Ordinal),
            "CheckInGuestMutation carries neither attribute");

        PragmaticUseCases.RulesWithoutAUseCase.Should().BeEmpty(
            "every [Rule] in this module sits beside a [UseCase]");
    }

    /// <summary>The document a person reads, generated from the same declarations.</summary>
    [Fact]
    public void TheMarkdownCatalog_ReadsAsASpecification()
    {
        PragmaticUseCases.Markdown.Should().StartWith("# Use cases");
        PragmaticUseCases.Markdown.Should().Contain("## BKG-CANCEL — Cancel a reservation");
        PragmaticUseCases.Markdown.Should().Contain(
            "- Only a pending reservation can be confirmed");
    }
}
