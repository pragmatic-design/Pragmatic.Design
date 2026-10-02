using System.Globalization;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
///     Asking for a pipe the registry does not have says what the registry has, and where
///     the ones it does not have live.
/// </summary>
/// <remarks>
///     <para>
///         <c>date</c>, <c>currency</c> and <c>percent</c> exist, in
///         <c>Pragmatic.Documents.Templating.I18N</c>, and are added with
///         <c>PipeRegistry.Default.WithI18N()</c>. The built-in set is a closed list of five, and a bare
///         <c>Unknown pipe: 'date'</c> names the pipe and not the package that has it.
///     </para>
///     <para>
///         A reader of <c>BuiltInPipes</c> finds no date formatting and writes a pipe of their own. The
///         extension point is not the problem; the message at the point of use is.
///     </para>
///     <para>
///         ⚠️ <b>The base package cannot reference the I18N one</b> — the dependency runs the other way —
///         so what it carries is the three <em>names</em>. A list of names in one package about pipes
///         declared in another is exactly the kind that ages in silence, so the test that pins it lives
///         in the I18N suite, where both are visible:
///         <c>TheHintNamesEveryPipeThisPackageAddsTests</c>.
///     </para>
/// </remarks>
public class AnUnknownPipeSaysWhereToFindOneTests
{
    /// <summary>The setpoint: the name that is somewhere else says where.</summary>
    [Theory]
    [InlineData("date")]
    [InlineData("currency")]
    [InlineData("percent")]
    public void APipeThatLivesInTheI18NPackage_NamesThePackageAndTheCall(string pipe)
    {
        var failed = Assert.Throws<InvalidOperationException>(
            () => PipeRegistry.Default.Execute(pipe, "anything", [], CultureInfo.InvariantCulture));

        failed.Message.Should().Contain("Pragmatic.Documents.Templating.I18N",
            "the package that has it is the one thing the reader cannot guess");
        failed.Message.Should().Contain("WithI18N",
            "and the call that adds it, because referencing the package alone changes nothing");
    }

    /// <summary>
    ///     The control: a name that is nobody's says what the registry does have, and does not point at
    ///     a package that would not help.
    /// </summary>
    [Fact]
    public void APipeThatIsNobodys_ListsWhatIsRegistered_AndPointsNowhere()
    {
        var failed = Assert.Throws<InvalidOperationException>(
            () => PipeRegistry.Default.Execute("reverse", "anything", [], CultureInfo.InvariantCulture));

        failed.Message.Should().Contain("reverse", "the name that was asked for");
        failed.Message.Should().Contain("number").And.Contain("uppercase").And.Contain("default",
            "the registered pipes, because a typo is the likeliest reason to be here");
        failed.Message.Should().NotContain("WithI18N",
            "pointing at a package that does not have it either would be a wrong answer, not a helpful one");
    }

    /// <summary>
    ///     And once the pipe is registered — by <c>WithI18N</c> or by an application of its own — the
    ///     hint is gone, because the name resolves.
    /// </summary>
    [Fact]
    public void APipeThatWasAdded_IsNotReportedAsMissing()
    {
        var registry = PipeRegistry.Default.With(new SomebodysDatePipe());

        registry.Execute("date", "anything", [], CultureInfo.InvariantCulture)
            .Should().Be("a date", "the registry the application built is the one that answers");
    }

    /// <summary>
    ///     And it is the message an application actually meets: through an evaluated expression, which
    ///     is the only way a template reaches a pipe.
    /// </summary>
    /// <remarks>
    ///     Asserted one layer out because that is where the reader is. <c>EvaluatePipe</c> is a single
    ///     call to <c>Execute</c>, so nothing reframes the failure on the way — and this test is what
    ///     says so rather than a reading of the code.
    /// </remarks>
    [Fact]
    public async Task TheMessageArrivesThroughAnEvaluatedExpression()
    {
        var evaluator = new ExpressionEvaluator();
        var expression = ExpressionParser.ParseExpression("when | date:\"yyyy-MM-dd\"");
        var context = new TemplateDataContext();
        context.AddSource("when", DateTimeOffset.UnixEpoch);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await evaluator.EvaluateToStringAsync(expression, context));

        failed.Message.Should().Contain("WithI18N",
            "a template is where the name is written, so it is where the answer has to arrive");
    }

    private sealed class SomebodysDatePipe : ITemplatePipe
    {
        public string Name => "date";

        public object? Execute(object? input, IReadOnlyList<string> args, CultureInfo culture)
            => "a date";
    }
}
