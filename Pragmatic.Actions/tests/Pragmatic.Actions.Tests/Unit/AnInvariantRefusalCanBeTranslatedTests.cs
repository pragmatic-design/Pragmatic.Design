using Pragmatic.Actions.Mutation;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     The refusal of an aggregate invariant carries a key the localization layer can read.
/// </summary>
/// <remarks>
///     <para>
///         The resolver asks <c>context is Error</c> and takes that error's <c>MessageKey</c>; anything
///         else falls back to a key derived from the <c>Code</c>. <c>InvariantViolationError</c>
///         implemented <c>IError</c> directly, so it took the fallback — and the fallback is one key for
///         every invariant in the application, <c>error.invariant.violation</c>, which cannot say which
///         rule refused.
///     </para>
///     <para>
///         So the type is an <c>Error</c> now, and the key the rule declares is the key it reports.
///     </para>
/// </remarks>
public class AnInvariantRefusalCanBeTranslatedTests
{
    [Fact]
    public void TheError_IsAnError_SoTheResolverReadsItsKey()
        => new InvariantViolationError("HasLines", "An order has at least one line")
            .Should().BeAssignableTo<Error>();

    [Fact]
    public void AKeyTheRuleDeclares_IsTheKeyItReports()
        => new InvariantViolationError("HasLines", "An order has at least one line", "validation.order.has_lines")
            .MessageKey.Should().Be("validation.order.has_lines");

    /// <summary>
    ///     The control: a rule that declares no key reports the derived one, which is what every
    ///     invariant reported before. Nothing was taken away from an application that has no translations.
    /// </summary>
    [Fact]
    public void ARuleThatDeclaresNoKey_ReportsTheDerivedOne()
        => new InvariantViolationError("HasLines", "An order has at least one line")
            .MessageKey.Should().Be("error.invariant.violation");

    /// <summary>The sentence is still the title: a host with no translation for the key answers it.</summary>
    [Fact]
    public void TheSentence_IsStillTheTitle()
        => new InvariantViolationError("HasLines", "An order has at least one line", "validation.order.has_lines")
            .Title.Should().Be("An order has at least one line");

    [Fact]
    public void WithNoSentence_TheTitleNamesTheRule()
        => new InvariantViolationError("HasLines", "")
            .Title.Should().Be("Invariant 'HasLines' was violated.");

    [Fact]
    public void TheStatus_IsUnprocessable()
    {
        var error = new InvariantViolationError("HasLines", "");

        error.StatusCode.Should().Be(422);
        error.Code.Should().Be("INVARIANT_VIOLATION");
    }
}
