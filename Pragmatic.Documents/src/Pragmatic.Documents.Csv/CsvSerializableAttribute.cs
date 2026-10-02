namespace Pragmatic.Documents.Csv;

/// <summary>
/// Marks a type for CSV source generation. The SG generates a nested <c>Csv</c> class
/// with typed <c>Write</c>, <c>Read</c>, and <c>Headers</c> members — zero reflection, AOT-safe.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CsvSerializableAttribute : Attribute;
