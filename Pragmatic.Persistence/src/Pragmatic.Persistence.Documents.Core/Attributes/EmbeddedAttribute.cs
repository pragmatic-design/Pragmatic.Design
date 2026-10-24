namespace Pragmatic.Persistence.Documents.Attributes;

/// <summary>A type stored inside its document (a property of that type is a nested document; a list of it, an array). The class is <c>partial</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EmbeddedAttribute : Attribute;
