using System.Text.Json;
using Pragmatic.Result.Fuzz;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     Every input the Result fuzz harness keeps, run through it in the gate (#312): the seeds a nightly run starts
///     from, and the regressions, each an input that once crashed. The harness throws on anything but a declared
///     outcome.
/// </summary>
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
