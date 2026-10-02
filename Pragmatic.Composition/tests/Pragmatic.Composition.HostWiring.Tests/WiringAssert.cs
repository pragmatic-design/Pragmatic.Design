// Pragmatic.Composition.HostWiring.Tests - Assertion helper

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     The one assertion this suite makes: <i>declared in the host produces the same registration as
///     declared in a library</i>.
/// </summary>
internal static class WiringAssert
{
    /// <summary>
    ///     Fails unless every wiring line the control group emits for <paramref name="feature" /> is
    ///     also emitted by the host that declares the same types itself.
    /// </summary>
    /// <param name="fixture">The two compiled hosts.</param>
    /// <param name="feature">Feature name, for the failure message.</param>
    /// <param name="hintName">Generated file the wiring belongs to, e.g. <c>Host.Services.g.cs</c>.</param>
    /// <param name="selects">
    ///     Picks the lines that belong to this feature. Applied to whole trimmed lines and compared as
    ///     whole trimmed lines — a <c>Contains("class X")</c> style assertion is also satisfied by
    ///     <c>class XDrifted</c>, which has already produced a false green in this codebase.
    /// </param>
    public static void RegistrationReachesHost(
        HostWiringFixture fixture,
        string feature,
        string hintName,
        Func<string, bool> selects)
    {
        var expected = HostWiringFixture.LinesOf(fixture.Control, hintName).Where(selects).ToList();

        // The control group is the part that makes the measurement mean anything. If it is empty the
        // library shape stopped emitting this wiring, and the subject's silence proves nothing.
        expected.Count.Should().BeGreaterThan(0,
            $"the control group must emit {feature} wiring in {hintName} — a host that references a "
            + "library declaring these types is the shape that works today. Empty means either the "
            + $"probe no longer exercises {feature}, or the library never compiled{DescribeLibraryHealth(fixture)}");

        var actual = HostWiringFixture.LinesOf(fixture.Subject, hintName).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(line => !actual.Contains(line)).ToList();

        var whereFrom = fixture.Subject.ContainsKey(hintName)
            ? $"Missing from the host's {hintName}"
            : $"The host generated no {hintName} at all; the control group's lines are";

        missing.Count.Should().Be(0,
            $"{feature} declared in the host project must be wired exactly as when declared in a "
            + $"library. {whereFrom}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, missing.Select(l => $"  - {l}")));
    }

    /// <summary>
    ///     Fails unless <b>neither</b> shape emits wiring for <paramref name="feature" />. Records a
    ///     channel that is unconsumed in both directions — a different defect from the one this suite
    ///     measures, and one that a "declared in host equals declared in library" assertion would
    ///     wrongly report as healthy.
    /// </summary>
    /// <remarks>
    ///     No callers today, and that is the point: every channel it recorded has since been wired —
    ///     Caching (the reader compared a category name against an ordinal), Privacy, and Configuration.
    ///     Kept because the next unconsumed channel needs recording the same way, and because an
    ///     absence written down is the only kind a test suite can notice.
    /// </remarks>
    public static void ChannelUnconsumedInBothShapes(
        HostWiringFixture fixture,
        string feature,
        string hintName,
        Func<string, bool> selects)
    {
        var inControl = HostWiringFixture.LinesOf(fixture.Control, hintName).Where(selects).ToList();
        var inSubject = HostWiringFixture.LinesOf(fixture.Subject, hintName).Where(selects).ToList();

        (inControl.Count + inSubject.Count).Should().Be(0,
            $"{feature} is recorded here as unwired in BOTH shapes, for a defect of its own. Wiring "
            + $"now appears in {hintName}, so that is no longer true and this test has become the "
            + $"wrong record:{Environment.NewLine}"
            + string.Join(Environment.NewLine, inControl.Select(l => $"  - control: {l}")
                .Concat(inSubject.Select(l => $"  - host:    {l}"))));
    }

    private static string DescribeLibraryHealth(HostWiringFixture fixture)
    {
        if (fixture.LibraryEmitErrors.Length == 0 && fixture.LibraryErrors.Length == 0)
            return " (the library compiled and emitted cleanly, so the probe is the suspect)";

        var errors = fixture.LibraryEmitErrors.Length > 0 ? fixture.LibraryEmitErrors : fixture.LibraryErrors;
        return $". The library did not compile:{Environment.NewLine}"
               + string.Join(Environment.NewLine, errors.Take(10).Select(e => $"  - {e}"));
    }
}
