// =============================================================================
// Pragmatic.Design - AccessModifier
// Enum for C# access modifiers
// =============================================================================

namespace Pragmatic.SourceGen;

/// <summary>
///     C# access modifiers for generated code.
/// </summary>
internal enum AccessModifier
{
    Public,
    Private,
    Protected,
    Internal,
    ProtectedInternal,
    PrivateProtected,
    NotApplicable
}

/// <summary>
///     Extension methods for AccessModifier.
/// </summary>
internal static class AccessModifierExtensions
{
    /// <summary>
    ///     Converts the access modifier to its C# keyword representation.
    /// </summary>
    public static string ToKeyword(this AccessModifier modifier)
    {
        return modifier switch
        {
            AccessModifier.Public => "public",
            AccessModifier.Private => "private",
            AccessModifier.Protected => "protected",
            AccessModifier.Internal => "internal",
            AccessModifier.ProtectedInternal => "protected internal",
            AccessModifier.PrivateProtected => "private protected",
            AccessModifier.NotApplicable => "",
            _ => ""
        };
    }
}