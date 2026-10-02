// =============================================================================
// Pragmatic.Design - Generator Model
// Base class for generator data models
// =============================================================================

namespace Pragmatic.SourceGen;

/// <summary>
///     Base class for generator data models that need equality comparison for caching.
/// </summary>
internal abstract record GeneratorModel
{
    /// <summary>
    ///     The namespace of the target type. Empty string for global namespace.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     The name of the target type.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public required string Accessibility { get; init; }

    /// <summary>
    ///     The type kind (class, struct, record).
    /// </summary>
    public required string TypeKind { get; init; }

    /// <summary>
    ///     Gets the fully qualified type name (Namespace.TypeName or just TypeName if no namespace).
    /// </summary>
    public string FullTypeName => string.IsNullOrEmpty(Namespace) ? TypeName : $"{Namespace}.{TypeName}";
}