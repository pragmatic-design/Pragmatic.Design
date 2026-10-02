using System.Reflection;
using System.Text.RegularExpressions;
using Pragmatic.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Invoicing.IntegrationTests;

/// <summary>
///     The counts this example's README quotes are the ones its own suite produces.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The README said <b>147 tests</b> where the suite ran <b>154 + 3</b>, and split them
///         <b>95 + 55</b>, which is 150 — three numbers that could not all be true, in the section whose
///         whole claim is that they were measured. A reader who runs the one command it gives gets a
///         different answer on the first try.
///     </para>
///     <para>
///         So the numbers a test can check are checked here instead of being re-read by whoever
///         remembers to. Not the wall-clock duration, which is a property of the machine, and not the
///         total — xUnit does not hand a test the size of its own run. What is left is the two counts
///         that come from the application (the generated contracts, the mapped routes) and the
///         arithmetic of the sentence, which is where the contradiction actually was.
///     </para>
/// </remarks>
public sealed class TheReadmeQuotesWhatTheSuiteMeasures
{
    /// <summary>
    ///     The example's README, copied beside the binary by this project's <c>Content</c> item.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not found by walking up from <c>AppContext.BaseDirectory</c> to the source tree: the gate
    ///     builds into an output folder of its own, so that walk reaches the README under
    ///     <c>dotnet test</c> and reaches nothing under <c>scripts/check.mjs</c>, where this type's
    ///     initializer would fail.
    /// </remarks>
    private static readonly string Readme =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "README.md"));

    /// <summary>The generated contract tests, counted the way the README says it counted them.</summary>
    /// <remarks>
    ///     Through reflection rather than by reading <c>obj/</c>: the assembly under test is the build
    ///     that is running, and <c>obj/…/generated</c> keeps files from earlier compilations — counting
    ///     those counts a mixture, and a README counted that way can quote a class that no longer exists.
    /// </remarks>
    private static readonly int GeneratedContracts =
        typeof(TheReadmeQuotesWhatTheSuiteMeasures).Assembly.GetTypes()
            .Where(type => typeof(PragmaticContractTestBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Count(method => method.GetCustomAttributes().Any(a => a.GetType().Name == "FactAttribute"));

    [Fact]
    public void TheGeneratedCountIsTheNumberOfGeneratedTests()
        => Quoted(@"\*\*(\d+) of those (\d+) tests are hand-written and (\d+) are generated\*\*", group: 3)
            .Should().Be(GeneratedContracts);

    /// <summary>And hand-written plus generated is the total the sentence claims.</summary>
    /// <remarks>
    ///     ⚠️ An arithmetic that does not add up is the cheapest possible signal that a number was edited and
    ///     its neighbours were not.
    /// </remarks>
    [Fact]
    public void TheSplitAddsUpToTheTotalItQuotes()
    {
        const string sentence = @"\*\*(\d+) of those (\d+) tests are hand-written and (\d+) are generated\*\*";

        (Quoted(sentence, group: 1) + Quoted(sentence, group: 3)).Should().Be(Quoted(sentence, group: 2));
    }

    /// <summary>The routes it quotes are the routes the application maps.</summary>
    /// <remarks>
    ///     The access table in <see cref="SigningInWithTheProvider" /> is already asserted against the
    ///     endpoints ASP.NET mapped, so quoting its length is quoting the application.
    /// </remarks>
    [Fact]
    public void TheRouteCountIsTheSizeOfTheAccessTable()
        => Quoted(@"behind\s+\*\*(\d+) routes\*\*", group: 1)
            .Should().Be(SigningInWithTheProvider.RouteCount);

    /// <summary>
    ///     The control: the patterns above still match something. A regex that matches nothing would make
    ///     every test here pass on a README that no longer says any of it.
    /// </summary>
    [Fact]
    public void TheSentencesTheseTestsReadAreStillInTheReadme()
    {
        Readme.Should().MatchRegex(@"\*\*\d+ of those \d+ tests are hand-written and \d+ are generated\*\*");
        Readme.Should().MatchRegex(@"behind\s+\*\*\d+ routes\*\*");
    }

    private static int Quoted(string pattern, int group)
    {
        var match = Regex.Match(Readme, pattern, RegexOptions.None, TimeSpan.FromSeconds(5));
        match.Success.Should().BeTrue("the README still carries the sentence '{0}'", pattern);

        return int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
