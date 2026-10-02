namespace Pragmatic.Testing;

/// <summary>Thrown when a Pragmatic test assertion fails. Carries a human-readable explanation.</summary>
public sealed class PragmaticTestAssertionException(string message) : Exception(message);
