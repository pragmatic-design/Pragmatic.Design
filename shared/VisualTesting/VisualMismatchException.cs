// ReSharper disable once CheckNamespace
namespace Pragmatic.Testing.Visual;

/// <summary>A comparison that failed its thresholds. The message names the metric, the bound and the worst region.</summary>
public sealed class VisualMismatchException(string message) : Exception(message);
