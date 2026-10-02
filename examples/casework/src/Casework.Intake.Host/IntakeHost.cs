namespace Casework.Intake.Host;

/// <summary>
///     The name the integration suite gives this process when it boots it.
/// </summary>
/// <remarks>
///     ⚠️ It exists because there are <b>two</b> hosts. <c>WebApplicationFactory&lt;TEntryPoint&gt;</c>
///     uses its type argument to find the assembly whose entry point it should run, and the first two
///     examples pass <c>Program</c> — which top-level statements put in the <b>global</b> namespace. With
///     one host that reads fine; with two it is either ambiguous or quietly the wrong one, and "quietly
///     the wrong one" would mean a suite that boots Intake twice and calls it a distributed test. A named
///     public type per host says which process is meant, at the call site.
/// </remarks>
public sealed class IntakeHost;
