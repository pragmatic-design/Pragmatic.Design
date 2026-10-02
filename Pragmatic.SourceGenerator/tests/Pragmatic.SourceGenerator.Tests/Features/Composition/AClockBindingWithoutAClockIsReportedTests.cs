using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     An operation that takes a value from the clock needs an <c>IClock</c>, and only the host can
///     register one. A host with no <c>Pragmatic.Temporal</c> is told at build time instead of at the
///     first request.
/// </summary>
/// <remarks>
///     <para>
///         Measured in the Invoicing example: <c>IssueInvoiceAction</c> declares
///         <c>[FromClock]</c>, the generated invoker resolves <c>IClock</c> through
///         <c>GetRequiredService</c>, and the route answered <b>500</b> — "No service for type
///         'Pragmatic.Temporal.Clock.IClock' has been registered" — on the one operation that needed a
///         date.
///     </para>
///     <para>
///         ⚠️ The declaration and the implementation live in different packages, which is what makes the
///         trap. <c>[FromClock]</c> and <c>IClock</c> are in <c>Pragmatic.Abstractions</c>, so any module
///         can declare one; <c>SystemClock</c> and <c>AddPragmaticTemporal</c> are in
///         <c>Pragmatic.Temporal</c>, which is <b>not</b> in the default host package set. So the host
///         cannot register what it cannot name — and reporting it is the only answer left.
///     </para>
/// </remarks>
public class AClockBindingWithoutAClockIsReportedTests
{
    private const string Diagnostic = "PRAG1698";

    /// <summary>
    ///     The attributes are declared in source: the generator matches them by full name, and this
    ///     compilation has neither Abstractions nor Temporal — which is the point.
    /// </summary>
    private const string Stubs = """
        using System;

        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.Temporal.Clock
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class FromClockAttribute : Attribute { }
        }
        namespace Pragmatic.Actions.Attributes
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class DomainActionAttribute : Attribute { }
        }
        public static class Program
        {
            public static void Main() { }
        }
        """;

    /// <summary>The clock's own package, present or absent — what the host can name.</summary>
    private const string TemporalPresence = """

        namespace Pragmatic.Temporal.Clock
        {
            public class SystemClock { }
        }
        """;

    private const string Operation = """

        namespace TestApp
        {
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Temporal.Clock;

            [DomainAction]
            public partial class IssueInvoiceAction
            {
                [FromClock]
                public System.DateOnly IssuedOn { get; private set; }
            }
        }
        """;

    [Fact]
    public void AHostWithNoClockPackage_IsToldAtBuildTime()
        => IdsOf(RunAsHost(Stubs + Operation))
            .Should().Contain(Diagnostic,
                "the invoker resolves IClock with GetRequiredService, and nothing in this host registers one");

    /// <summary>The control: with the package there, the generated host registers the clock and says nothing.</summary>
    [Fact]
    public void AHostThatCanNameTheClock_IsNotReported()
        => IdsOf(RunAsHost(Stubs + TemporalPresence + Operation))
            .Should().NotContain(Diagnostic,
                "AddPragmaticTemporal() is emitted for this host, so the requirement is met");

    /// <summary>The second control: no [FromClock] anywhere, no report.</summary>
    [Fact]
    public void AHostWithNothingAskingForAClock_IsNotReported()
        => IdsOf(RunAsHost(Stubs)).Should().NotContain(Diagnostic);

    /// <summary>
    ///     And a module says it needs one, so a host that references it can answer for it. This is the
    ///     half that crosses the assembly boundary: the declaration is in the module, the registration
    ///     is the host's, and metadata is what travels.
    /// </summary>
    [Fact]
    public void AModuleThatTakesTheClock_SaysSoInItsMetadata()
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + Operation, []));

        sources.Keys.Should().Contain(k => k.Contains("_Metadata.ClockBindings"),
            "a host reads the categories of what it references, not the syntax of somebody else's source");
    }

    private static SourceGenRunResult RunAsHost(string source)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source, []);

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
