using System.Linq;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Composition;

/// <summary>
///     PRAG1616 (#747): an operation refused when a feature flag is off needs a host that evaluates flags, and a
///     host with no <c>Pragmatic.FeatureFlags</c> is told at build time, since the gate fails closed.
/// </summary>
public class AFeatureGateWithoutFeatureFlagsIsReportedTests
{
    private const string Diagnostic = "PRAG1616";

    /// <summary>The attributes are declared in source: the generator matches them by full name.</summary>
    private const string Stubs = """
        using System;

        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }
        namespace Pragmatic.FeatureFlags
        {
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class RequiresFeatureAttribute(string flagName) : Attribute { public string FlagName { get; } = flagName; }
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

    /// <summary>The type the feature detector reads feature flags from.</summary>
    private const string FeatureFlagsPresence = """

        namespace Pragmatic.FeatureFlags
        {
            public static class FeatureFlagServiceCollectionExtensions { }
        }
        """;

    private const string Operation = """

        namespace TestApp
        {
            using Pragmatic.Actions.Attributes;
            using Pragmatic.FeatureFlags;

            [DomainAction]
            [RequiresFeature("escalations")]
            public partial class EscalateAction { }
        }
        """;

    [Fact]
    public void AHostWithNoFeatureFlags_IsToldAtBuildTime()
        => IdsOf(RunAsHost(Stubs + Operation)).Should().Contain(Diagnostic);

    /// <summary>The control: with feature flags there, nothing is said.</summary>
    [Fact]
    public void AHostThatEvaluatesFlags_IsNotReported()
        => IdsOf(RunAsHost(Stubs + FeatureFlagsPresence + Operation)).Should().NotContain(Diagnostic);

    /// <summary>The second control: no [RequiresFeature] anywhere, no report.</summary>
    [Fact]
    public void AHostWithNothingGated_IsNotReported()
        => IdsOf(RunAsHost(Stubs)).Should().NotContain(Diagnostic);

    [Fact]
    public void AModuleWithAGatedOperation_SaysSoInItsMetadata()
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(
            GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Stubs + Operation, []));

        sources.Keys.Should().Contain(k => k.Contains("_Metadata.FeatureGates"));
    }

    private static SourceGenRunResult RunAsHost(string source)
        => GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(source, []);

    private static string[] IdsOf(SourceGenRunResult result)
        => [.. result.Diagnostics.Select(d => d.Id)];
}
