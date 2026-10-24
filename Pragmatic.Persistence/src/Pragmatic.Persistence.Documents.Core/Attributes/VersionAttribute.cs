namespace Pragmatic.Persistence.Documents.Attributes;

/// <summary>An integer the writes check and increment: a replace or delete of a stale copy is refused (<see cref="DocConcurrencyException" />).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VersionAttribute : Attribute;
