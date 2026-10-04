namespace Pragmatic.Logging.Benchmarks.Redaction;

/// <summary>A logged value with one member its type declared must not be logged.</summary>
public sealed record Customer(string Reference, string Email, int Orders);
