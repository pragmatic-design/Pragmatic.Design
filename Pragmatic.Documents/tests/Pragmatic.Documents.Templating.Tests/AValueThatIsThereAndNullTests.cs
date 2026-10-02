using Pragmatic.Documents.Templating.Data;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
///     A property that exists and is null is not the same thing as a property that does not
///     exist, and only one of the two is worth warning about.
/// </summary>
/// <remarks>
///     <para>
///         <b>The warnings are a channel an application is told to read.</b> A missing property is not an
///         error — the expression resolves to null and the resolution is recorded — so reading them is
///         the only way to tell "the template resolved" from "the data was there".
///     </para>
///     <para>
///         ⚠️ Warning on <c>result is null</c> would make a case with no decision date yet produce the
///         same warning as a template naming a property nobody has ever provided. An application that
///         failed its build on warnings could not distinguish them, and one that ignored them would be
///         ignoring both, including on a correct letter whose null values are legitimately empty.
///     </para>
///     <para>
///         <c>IPropertyAccessor.HasProperty</c> answers the question.
///     </para>
/// </remarks>
public class AValueThatIsThereAndNullTests
{
    [Fact]
    public async Task APropertyThatIsPresentAndNull_DoesNotWarn()
    {
        var data = new TemplateDataContext()
            .AddSource("case", new Dictionary<string, object?>
            {
                ["number"] = "CASE-00001",
                // The case has not been decided yet. The template asks for it, the letter shows a blank,
                // and that is the application's own business rather than a defect.
                ["decidedOn"] = null
            });

        (await data.ResolveAsync("case.decidedOn")).Should().BeNull();

        data.Warnings.Should().BeEmpty(
            "the property is there and empty, which is a fact about the data and not about the template");
    }

    [Fact]
    public async Task APropertyThatIsNotThere_StillWarns()
    {
        var data = new TemplateDataContext()
            .AddSource("case", new Dictionary<string, object?> { ["number"] = "CASE-00001" });

        (await data.ResolveAsync("case.reference")).Should().BeNull();

        data.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("case.reference");
    }

    /// <summary>
    ///     The control: a path that breaks <b>before</b> its end is unresolved, not empty.
    /// </summary>
    /// <remarks>
    ///     Without it "do not warn on null" would be satisfied by never warning: a template naming
    ///     <c>case.decision.signedBy</c> when there is no decision at all has asked for something the
    ///     data cannot answer, and the walk stopped early rather than arriving at an empty value.
    /// </remarks>
    [Fact]
    public async Task APathThatBreaksHalfWay_Warns()
    {
        var data = new TemplateDataContext()
            .AddSource("case", new Dictionary<string, object?> { ["decision"] = null });

        (await data.ResolveAsync("case.decision.signedBy")).Should().BeNull();

        data.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("case.decision.signedBy");
    }
}
