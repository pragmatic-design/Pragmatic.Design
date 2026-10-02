namespace Pragmatic.Configuration.Management.Actions;

/// <summary>Result of a single config value query.</summary>
public sealed record ConfigValueResult(string Key, string? Value, string? TenantId);
