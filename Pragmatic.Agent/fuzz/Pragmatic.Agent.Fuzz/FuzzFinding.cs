namespace Pragmatic.Agent.Fuzz;

/// <summary>
///     An outcome a harness found that contradicts the input: thrown so that libFuzzer records it as a crash, with the
///     input that produced it.
/// </summary>
public sealed class FuzzFinding(string message) : Exception(message);
