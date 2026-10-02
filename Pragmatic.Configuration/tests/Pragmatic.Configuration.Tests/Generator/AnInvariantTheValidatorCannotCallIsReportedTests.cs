using Pragmatic.Testing.Assertions;

namespace Pragmatic.Configuration.Tests.Generator;

/// <summary>
///     A <c>[ConfigInvariant]</c> the generated validator cannot call is PRAG2002, on the method.
/// </summary>
/// <remarks>
///     The transform dropped it: a static method, one with a parameter, or one not returning <c>bool</c>
///     compiled, generated no check, and the rule it stated never ran at startup.
/// </remarks>
public class AnInvariantTheValidatorCannotCallIsReportedTests : ConfigurationGeneratorTestBase
{
    [Theory]
    [InlineData("public static bool EndAfterStart() => true;")]
    [InlineData("public bool EndAfterStart(int margin) => End > Start + margin;")]
    [InlineData("public string EndAfterStart() => \"\";")]
    public void AMisshapenInvariant_IsPRAG2002(string method)
    {
        var result = RunGenerator(Source(method));

        GetGeneratorDiagnostics(result).Where(d => d.Id == "PRAG2002").Should().ContainSingle()
            .Which.GetMessage().Should().Contain("EndAfterStart").And.Contain("ScheduleOptions");
    }

    /// <summary>The control: a callable invariant is generated and reported as nothing.</summary>
    [Fact]
    public void ACallableInvariant_IsNotReported()
    {
        var result = RunGenerator(Source("public bool EndAfterStart() => End > Start;"));

        HasDiagnostic(result, "PRAG2002").Should().BeFalse();
        GetGeneratedSourcesAsDictionary(result).Keys.Should().Contain(k => k.Contains("Validator"));
    }

    private static string Source(string method) => $$"""
        using Pragmatic.Configuration;

        namespace TestApp;

        [Configuration]
        public partial class ScheduleOptions
        {
            public int Start { get; set; }
            public int End { get; set; }

            [ConfigInvariant("End must be greater than Start")]
            {{method}}
        }
        """;
}
