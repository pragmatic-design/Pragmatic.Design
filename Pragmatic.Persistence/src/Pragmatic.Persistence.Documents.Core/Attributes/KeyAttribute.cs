namespace Pragmatic.Persistence.Documents.Attributes;

/// <summary>The document's identity, stored as <c>_id</c>.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class KeyAttribute : Attribute;
