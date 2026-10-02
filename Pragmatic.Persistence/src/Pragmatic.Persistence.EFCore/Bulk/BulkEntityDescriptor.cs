namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Source-generated descriptor for bulk operations on a specific entity type.
///     Contains column metadata and a zero-reflection value reader.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
public sealed class BulkEntityDescriptor<T> where T : class
{
    /// <summary>Column definitions with their roles.</summary>
    public required (string PropertyName, BulkColumnRole Role)[] Columns { get; init; }

    /// <summary>
    ///     Source-generated delegate that reads a property value from an entity by property name.
    ///     Returns the value as object? (boxing occurs but is unavoidable for ADO.NET parameters).
    /// </summary>
    public required Func<T, string, object?> ReadValue { get; init; }
}
