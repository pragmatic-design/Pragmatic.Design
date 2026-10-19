using System.Text.Json;
using Pragmatic.Persistence.Fuzz;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.AdapterTests;

/// <summary>
///     Every input the Persistence fuzz harnesses keep, run through its harness in the gate (#310): the seeds a
///     nightly run starts from, and the regressions, each an input that once crashed. A harness throws on anything
///     but a declared outcome.
/// </summary>
/// <remarks>
///     ⚠️ The ten-thousand-term regression ended the process when it crashed, a stack overflow no test can catch.
///     Replaying it here is safe only because the adapters now refuse it before building anything: if the bound
///     goes, this test takes the test host down with it, which is louder than a red test and means the same.
/// </remarks>
public class FuzzInputsReplayTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fuzz");

    public static TheoryData<string> Targets() => new(ManifestTargets());

    [Fact]
    public void The_manifest_and_the_harnesses_name_the_same_targets()
        => ManifestTargets().Should().BeEquivalentTo(FuzzTargets.All.Keys);

    private static string[] ManifestTargets()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "fuzz.json")));
        return [.. manifest.RootElement.GetProperty("targets").EnumerateObject().Select(target => target.Name)];
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void Every_kept_input_gets_a_declared_outcome(string target)
    {
        var seeds = Inputs("Corpus", target);
        seeds.Should().NotBeEmpty();

        foreach (var input in seeds.Concat(Inputs("Regressions", target)))
            FuzzTargets.All[target](File.ReadAllBytes(input));
    }

    private static string[] Inputs(string kind, string target)
    {
        var dir = Path.Combine(Root, kind, target);
        return Directory.Exists(dir) ? Directory.GetFiles(dir) : [];
    }
}
