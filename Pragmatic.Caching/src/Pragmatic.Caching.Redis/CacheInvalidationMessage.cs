namespace Pragmatic.Caching.Redis;

/// <summary>One invalidation, as it travels between nodes.</summary>
/// <param name="Kind">Whether <paramref name="Value" /> is a tag or a key.</param>
/// <param name="Value">The tag or the key.</param>
/// <param name="Origin">The node that ran it locally, so that node can ignore its own message.</param>
/// <remarks>
///     <para>
///         On the wire it is <c>{T|K}|{origin}|{value}</c>. A format this small needs no serializer —
///         and a reflective one on every invalidation is the kind of cost "no reflection in new code" exists to keep out. The
///         value goes last because it is the only part that may contain the separator: a tag or a key
///         is whatever the application wrote, and the split stops after the second <c>|</c>.
///     </para>
/// </remarks>
public sealed record CacheInvalidationMessage(CacheInvalidationKind Kind, string Value, string Origin)
{
    private const char Separator = '|';

    /// <summary>The message as it is published.</summary>
    public string ToWire()
        => $"{(Kind == CacheInvalidationKind.Tag ? 'T' : 'K')}{Separator}{Origin}{Separator}{Value}";

    /// <summary>
    ///     Reads a published message, or answers <see langword="null" /> for anything that is not one —
    ///     another publisher on the same channel, or a message from a version with another format.
    /// </summary>
    public static CacheInvalidationMessage? FromWire(string? wire)
    {
        if (string.IsNullOrEmpty(wire))
            return null;

        var parts = wire.Split(Separator, 3);
        if (parts.Length != 3 || parts[0].Length != 1 || parts[1].Length == 0 || parts[2].Length == 0)
            return null;

        CacheInvalidationKind? kind = parts[0][0] switch
        {
            'T' => CacheInvalidationKind.Tag,
            'K' => CacheInvalidationKind.Key,
            _ => null,
        };

        return kind is { } k ? new CacheInvalidationMessage(k, parts[2], parts[1]) : null;
    }
}
