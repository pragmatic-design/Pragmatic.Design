using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen.Testing;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     Shared assertion for the per-feature incrementality tests. Adding an UNRELATED class to the
///     compilation must leave a feature's tracked steps <c>Cached</c>/<c>Unchanged</c>: a re-execution
///     means the pipeline lost value equality somewhere — typically a raw <c>ImmutableArray&lt;T&gt;</c>
///     in a model (compares by array reference) or a raw <c>CompilationProvider</c> combined into a
///     stage that does not need it (the Compilation is a new object on every edit).
/// </summary>
internal static class IncrementalityAssert
{
    /// <summary>An addition that touches nothing any feature pipeline depends on.</summary>
    public const string UnrelatedAddition = """
        namespace Unrelated
        {
            public class TotallyUnrelatedType
            {
                public int Value { get; set; }
            }
        }
        """;

    public static void StepsStayCached(IncrementalRunResult result, params string[] trackingNames)
    {
        // Non-vacuity: GetReExecutedSteps silently returns nothing for a step that never ran, so a
        // source that fails to activate the feature would make the test pass without asserting anything.
        foreach (var name in trackingNames)
        {
            result.RunResult.TrackedSteps.ContainsKey(name).Should().BeTrue(
                $"the tracked step '{name}' must exist in the pipeline");

            result.RunResult.TrackedSteps[name]
                .SelectMany(step => step.Outputs)
                .Should().NotBeEmpty(
                    $"'{name}' produced no value — the test source does not actually exercise this feature");
        }

        GeneratorTestHelper.GetReExecutedSteps(result, trackingNames).Should().BeEmpty(
            "the unrelated class changes nothing these steps depend on, so they must stay cached");
    }
}
