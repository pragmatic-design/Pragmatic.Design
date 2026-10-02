using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.EFCore.Converters;

/// <summary>
///     EF Core value converter for storing Guid as ShortGuid string (22 characters).
/// </summary>
/// <remarks>
///     <para>
///         This converter enables storing Guid values as compact, URL-safe strings
///         in the database while keeping Guid as the CLR type.
///     </para>
///     <para>
///         Usage:
///     </para>
///     <code>
/// modelBuilder.Entity&lt;Order&gt;()
///     .Property(o => o.ExternalId)
///     .HasConversion&lt;ShortGuidConverter&gt;();
/// </code>
/// </remarks>
public sealed class ShortGuidConverter : ValueConverter<Guid, string>
{
    /// <summary>
    ///     Creates a new ShortGuidConverter.
    /// </summary>
    public ShortGuidConverter()
        : base(
            guid => ShortGuid.Encode(guid),
            str => ShortGuid.Decode(str))
    {
    }
}

/// <summary>
///     EF Core value converter for storing nullable Guid as ShortGuid string.
/// </summary>
public sealed class NullableShortGuidConverter : ValueConverter<Guid?, string?>
{
    /// <summary>
    ///     Creates a new NullableShortGuidConverter.
    /// </summary>
    public NullableShortGuidConverter()
        : base(
            guid => guid.HasValue ? ShortGuid.Encode(guid.Value) : null,
            str => str != null ? ShortGuid.Decode(str) : null)
    {
    }
}
