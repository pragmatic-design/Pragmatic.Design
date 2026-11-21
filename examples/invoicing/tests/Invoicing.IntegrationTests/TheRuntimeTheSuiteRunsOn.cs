namespace Invoicing.IntegrationTests;

/// <summary>
///     The suite runs once per target the application declares (#923), and each run is on the runtime of its target: the host runs in this
///     process, so the runtime the test sees is the one the application answers on.
/// </summary>
/// <remarks>
///     A run that claimed net11.0 and rolled onto another runtime would prove nothing about .NET 11; this is what says it did not.
/// </remarks>
public sealed class TheRuntimeTheSuiteRunsOn
{
#if NET11_0_OR_GREATER
    private const int TargetMajor = 11;
#else
    private const int TargetMajor = 10;
#endif

    [Fact]
    public void TheRun_IsOnTheRuntimeOfItsTarget()
        => Assert.Equal(TargetMajor, Environment.Version.Major);
}
