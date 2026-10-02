using System.Globalization;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Templating.I18N.Tests;

/// <summary>
///     The names the base package prints when a template asks for a pipe that lives here
///     are exactly the pipes <c>WithI18N()</c> adds.
/// </summary>
/// <remarks>
///     <para>
///         <c>PipeRegistry</c> cannot reference this package — the dependency runs the other way — so
///         the hint in its "unknown pipe" message is a list of <b>names</b>. A list in one package about
///         types declared in another is the kind that ages in silence: a fourth pipe added here would
///         leave an application reading "date, currency, percent" and concluding the fourth does not
///         exist, which is the defect this whole issue is about, arriving a second time.
///     </para>
///     <para>
///         This test is where both halves are visible, so it is where the list is pinned. It asserts
///         both directions: every pipe this package adds is named by the message, and the message names
///         nothing this package does not add.
///     </para>
/// </remarks>
public class TheHintNamesEveryPipeThisPackageAddsTests
{
    [Fact]
    public void EveryPipeWithI18NAdds_IsNamedByTheMessage()
    {
        var added = PipeRegistry.Default.WithI18N();

        foreach (var pipe in I18NTemplatingExtensions.I18NPipes.Select(p => p.Name))
        {
            added.Contains(pipe).Should().BeTrue($"WithI18N() adds '{pipe}'");

            var failed = Assert.Throws<InvalidOperationException>(
                () => PipeRegistry.Default.Execute(pipe, "anything", [], CultureInfo.InvariantCulture));

            failed.Message.Should().Contain("Pragmatic.Documents.Templating.I18N",
                $"'{pipe}' is added by this package, so the base message has to point at it");
            failed.Message.Should().Contain("WithI18N");
        }
    }

    /// <summary>
    ///     And the other direction: the parenthesised list in the message is this package's pipe names,
    ///     not a set that once was.
    /// </summary>
    [Fact]
    public void TheMessagesList_IsExactlyWhatThisPackageAdds()
    {
        var expected = string.Join(", ", I18NTemplatingExtensions.I18NPipes
            .Select(p => p.Name)
            .Order(StringComparer.OrdinalIgnoreCase));

        var failed = Assert.Throws<InvalidOperationException>(
            () => PipeRegistry.Default.Execute("date", "anything", [], CultureInfo.InvariantCulture));

        failed.Message.Should().Contain($"({expected})",
            "the message prints the list, so the list is assertable — and a pipe added here without "
            + "updating it makes this test red instead of making a reader believe it does not exist");
    }
}
