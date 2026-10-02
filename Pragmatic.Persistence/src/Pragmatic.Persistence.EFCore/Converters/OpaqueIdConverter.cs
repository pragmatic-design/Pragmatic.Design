using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.EFCore.Converters;

/// <summary>
///     EF Core value converter for storing long as OpaqueId string.
/// </summary>
/// <remarks>
///     <para>
///         This converter enables storing sequential long IDs as non-guessable strings
///         in the database while keeping long as the CLR type.
///     </para>
///     <para>
///         Usage:
///     </para>
///     <code>
/// modelBuilder.Entity&lt;Order&gt;()
///     .Property(o => o.PublicId)
///     .HasConversion(new OpaqueIdConverter("my-app-salt"));
/// </code>
/// </remarks>
public sealed class OpaqueIdConverter : ValueConverter<long, string>
{
    /// <summary>
    ///     Creates a new OpaqueIdConverter with the default salt.
    /// </summary>
    public OpaqueIdConverter()
        : this(new OpaqueId())
    {
    }

    /// <summary>
    ///     Creates a new OpaqueIdConverter with a custom salt.
    /// </summary>
    /// <param name="salt">The salt for encoding/decoding.</param>
    public OpaqueIdConverter(string salt)
        : this(new OpaqueId(salt))
    {
    }

    private OpaqueIdConverter(OpaqueId encoder)
        : base(
            id => encoder.Encode(id),
            str => encoder.Decode(str))
    {
    }
}

/// <summary>
///     EF Core value converter for storing nullable long as OpaqueId string.
/// </summary>
public sealed class NullableOpaqueIdConverter : ValueConverter<long?, string?>
{
    /// <summary>
    ///     Creates a new NullableOpaqueIdConverter with the default salt.
    /// </summary>
    public NullableOpaqueIdConverter()
        : this(new OpaqueId())
    {
    }

    /// <summary>
    ///     Creates a new NullableOpaqueIdConverter with a custom salt.
    /// </summary>
    /// <param name="salt">The salt for encoding/decoding.</param>
    public NullableOpaqueIdConverter(string salt)
        : this(new OpaqueId(salt))
    {
    }

    private NullableOpaqueIdConverter(OpaqueId encoder)
        : base(
            id => id.HasValue ? encoder.Encode(id.Value) : null,
            str => str != null ? encoder.Decode(str) : null)
    {
    }
}
