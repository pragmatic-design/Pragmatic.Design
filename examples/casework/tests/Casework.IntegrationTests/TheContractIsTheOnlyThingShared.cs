using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Casework.Intake.Events;
using Casework.Verify.Events;
using Pragmatic.Testing.Assertions;

namespace Casework.IntegrationTests;

/// <summary>
///     The two services share a contract and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         Read from the assemblies' own reference lists rather than from a <c>grep</c> over the csproj
///         files: what matters is what the compiler bound, and a reference can also arrive transitively
///         from a third project without any file naming it.
///     </para>
///     <para>
///         This is the assertion that keeps the example an example. The day somebody adds a
///         <c>ProjectReference</c> from Verify to <c>Casework.Intake</c> to reach an entity or an
///         operation, the two services stop being two services, and every other test here would still
///         pass.
///     </para>
/// </remarks>
public sealed class TheContractIsTheOnlyThingShared
{
    private static readonly Assembly Intake = typeof(Casework.Intake.IntakeModule).Assembly;
    private static readonly Assembly Verify = typeof(Casework.Verify.VerifyModule).Assembly;
    private static readonly Assembly IntakesContract = typeof(VerificationRequested).Assembly;
    private static readonly Assembly VerifysContract = typeof(VerificationAnswered).Assembly;

    [Fact]
    public void Verify_ReferencesIntakesContract()
    {
        ReferencesOf(Verify).Should().Contain(IntakesContract.GetName().Name!,
            "the consumer names the same type the publisher does — a message is deserialized by its "
            + "fully qualified type name, and two records of the same shape never meet on the wire");
    }

    /// <summary>
    ///     And the other direction: Intake consumes Verify's answer.
    /// </summary>
    /// <remarks>
    ///     Two contract assemblies and not one shared <c>Casework.Contracts</c>: a contract belongs to the
    ///     service that publishes it, and one assembly for both would be a place where either service
    ///     could quietly take a dependency on the other's shape while this test stayed green.
    /// </remarks>
    [Fact]
    public void Intake_ReferencesVerifysContract()
    {
        ReferencesOf(Intake).Should().Contain(VerifysContract.GetName().Name!,
            "the exchange has two directions, and each end names the other's contract");
    }

    /// <summary>
    ///     Verify's compiled assembly binds nothing of Intake's.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is weaker than it looks, and the control proved it: a <c>ProjectReference</c> whose
    ///     types nobody names is <b>elided</b> by the compiler, so this test stayed green with the
    ///     forbidden reference in the project file. It says "Verify uses nothing of Intake", which is
    ///     worth saying — and <see cref="TheProjectFiles_DeclareNoReferenceBetweenTheServices" /> says the
    ///     other half, the one that fails the moment somebody adds the line.
    /// </remarks>
    [Fact]
    public void Verify_BindsNothingOfIntake()
    {
        ReferencesOf(Verify).Should().NotContain(Intake.GetName().Name!,
            "Intake's entities and operations are its own; what crosses is a message");
    }

    [Fact]
    public void Intake_BindsNothingOfVerify()
    {
        ReferencesOf(Intake).Should().NotContain(Verify.GetName().Name!,
            "and the dependency does not exist in the other direction either");
    }

    /// <summary>
    ///     Neither service's project file names the other's project.
    /// </summary>
    /// <remarks>
    ///     The declared fact, read from the csproj, because the compiled one cannot see an unused
    ///     reference — and an unused reference is how a used one arrives tomorrow.
    /// </remarks>
    [Fact]
    public void TheProjectFiles_DeclareNoReferenceBetweenTheServices()
    {
        References("Casework.Verify").Should().NotContain(
            reference => reference.EndsWith("Casework.Intake.csproj", StringComparison.Ordinal),
            "Verify references the contract, never the module");

        References("Casework.Intake").Should().NotContain(
            reference => reference.EndsWith("Casework.Verify.csproj", StringComparison.Ordinal),
            "and Intake does not know Verify exists");
    }

    /// <summary>The <c>ProjectReference</c> paths a project declares.</summary>
    private static List<string> References(string project)
    {
        var file = Path.Combine(ExampleRoot(), "src", project, $"{project}.csproj");
        File.Exists(file).Should().BeTrue($"the project file has to be where this test looks: {file}");

        return Regex.Matches(File.ReadAllText(file), "Include=\"([^\"]+\\.csproj)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();
    }

    /// <summary>
    ///     The example's root: two directories above <b>this source file</b>.
    /// </summary>
    /// <remarks>
    ///     ⚠️ From <c>[CallerFilePath]</c> and not by walking up from <c>AppContext.BaseDirectory</c>,
    ///     which is where the first version looked and why it failed under the gate and nowhere else: the
    ///     gate builds into <c>artifacts/build/bin/…</c>, so no ancestor of the running assembly holds
    ///     <c>Casework.slnx</c>. The compiler knows where the source is; the output path is the build's
    ///     business and moves.
    /// </remarks>
    private static string ExampleRoot([CallerFilePath] string thisFile = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

        File.Exists(Path.Combine(root, "Casework.slnx")).Should().BeTrue(
            $"two directories above this source file is the example's root: {root}");

        return root;
    }

    /// <summary>
    ///     Each contract carries the framework's markers and nothing of either service: a consumer takes
    ///     that assembly, and with it only what a contract needs.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Both of them, because the second one is where the shortcut would be taken: Verify's answer
    ///     carries an outcome, and the easy way to write that enum is in Verify's module — which would put
    ///     the module in the contract's reference list and hand it to Intake.
    /// </remarks>
    [Theory]
    [InlineData("Casework.Intake.Contracts")]
    [InlineData("Casework.Verify.Contracts")]
    public void AContract_ReferencesNeitherService(string contract)
    {
        var assembly = contract == "Casework.Intake.Contracts" ? IntakesContract : VerifysContract;
        var references = ReferencesOf(assembly);

        references.Should().NotContain(Intake.GetName().Name!);
        references.Should().NotContain(Verify.GetName().Name!);
        references.Should().Contain("Pragmatic.Abstractions", "which is where IIntegrationEvent is declared");
    }

    private static List<string> ReferencesOf(Assembly assembly)
        => assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToList();
}
