using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Persistence.Sql.Execution;

/// <summary>
///     Cursors as opaque tokens for an API: the values of a keyset cursor (one, or a tuple of up to four) written in a
///     compact binary form, signed with HMAC-SHA256 under the application's key, Base64Url-encoded (<c>?after=…</c> safe
///     as it is). <see cref="Decode{T1}" /> and its tuple forms give the cursor back as the typed value <c>After</c> and
///     <c>Before</c> take, or throw <see cref="SqlCursorException" /> when the token was altered, signed by another key or
///     purpose, or holds another shape. A purpose (the endpoint's name) keeps a token of one listing from paging another.
///     Values are Guid, string, bool, the integers, decimal, double, float, DateTimeOffset, DateTime, DateOnly, TimeOnly,
///     byte[], an enum, and null; the token carries no type names, only a tag per value.
/// </summary>
public sealed partial class SqlCursorSigner
{
    private const byte Version = 1;
    private const int TagLength = 16; // half of the HMAC: 128 bits, as a URL-sized token wants
    private readonly byte[] _key;
    private readonly byte[] _purpose;

    /// <param name="key">The signing key, at least 16 bytes; the same key decodes what it encoded.</param>
    /// <param name="purpose">What the tokens page (an endpoint's name): a token decodes only under the purpose it was made with.</param>
    public SqlCursorSigner(byte[] key, string purpose = "")
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(purpose);
        if (key.Length < 16)
            throw new ArgumentException("The signing key has at least 16 bytes.", nameof(key));
        _key = (byte[])key.Clone();
        _purpose = Encoding.UTF8.GetBytes(purpose);
    }

    public string Encode<T1>(T1 value)
        => Seal(writer => { Item(writer, value); }, 1);

    public string Encode<T1, T2>((T1, T2) cursor)
        => Seal(writer => { Item(writer, cursor.Item1); Item(writer, cursor.Item2); }, 2);

    public string Encode<T1, T2, T3>((T1, T2, T3) cursor)
        => Seal(writer => { Item(writer, cursor.Item1); Item(writer, cursor.Item2); Item(writer, cursor.Item3); }, 3);

    public string Encode<T1, T2, T3, T4>((T1, T2, T3, T4) cursor)
        => Seal(writer => { Item(writer, cursor.Item1); Item(writer, cursor.Item2); Item(writer, cursor.Item3); Item(writer, cursor.Item4); }, 4);

    public T1 Decode<T1>(string token)
    {
        using var reader = Open(token, 1);
        return Item<T1>(reader, 1);
    }

    public (T1, T2) Decode<T1, T2>(string token)
    {
        using var reader = Open(token, 2);
        return (Item<T1>(reader, 1), Item<T2>(reader, 2));
    }

    public (T1, T2, T3) Decode<T1, T2, T3>(string token)
    {
        using var reader = Open(token, 3);
        return (Item<T1>(reader, 1), Item<T2>(reader, 2), Item<T3>(reader, 3));
    }

    public (T1, T2, T3, T4) Decode<T1, T2, T3, T4>(string token)
    {
        using var reader = Open(token, 4);
        return (Item<T1>(reader, 1), Item<T2>(reader, 2), Item<T3>(reader, 3), Item<T4>(reader, 4));
    }

    /// <summary>The payload (version, count, the items) followed by the first half of its HMAC over the purpose and the payload, as Base64Url.</summary>
    private string Seal(Action<BinaryWriter> items, byte count)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);
            writer.Write(count);
            items(writer);
        }

        var payload = stream.GetBuffer().AsSpan(0, (int)stream.Length);
        var token = new byte[payload.Length + TagLength];
        payload.CopyTo(token);
        Signature(payload).CopyTo(token.AsSpan(payload.Length));
        return Base64Url.EncodeToString(token);
    }

    /// <summary>The token's payload verified and opened past its header; the reader stands on the first item.</summary>
    private BinaryReader Open(string token, int count)
    {
        ArgumentNullException.ThrowIfNull(token);
        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            throw new SqlCursorException("The cursor token is not a Base64Url string.");
        }

        if (bytes.Length < 2 + TagLength)
            throw new SqlCursorException("The cursor token is too short.");
        var payload = bytes.AsSpan(0, bytes.Length - TagLength);
        if (!CryptographicOperations.FixedTimeEquals(Signature(payload), bytes.AsSpan(payload.Length)))
            throw new SqlCursorException("The cursor token was not issued here: its signature does not match (another key, another purpose, or altered).");
        if (payload[0] != Version)
            throw new SqlCursorException($"The cursor token is of version {payload[0]}; this signer reads version {Version}.");
        if (payload[1] != count)
            throw new SqlCursorException($"The cursor token holds {payload[1]} value{(payload[1] == 1 ? "" : "s")}, {count} expected.");
        var reader = new BinaryReader(new MemoryStream(bytes, 2, payload.Length - 2, writable: false), Encoding.UTF8);
        return reader;
    }

    private byte[] Signature(ReadOnlySpan<byte> payload)
    {
        var signed = new byte[_purpose.Length + payload.Length];
        _purpose.CopyTo(signed, 0);
        payload.CopyTo(signed.AsSpan(_purpose.Length));
        return HMACSHA256.HashData(_key, signed).AsSpan(0, TagLength).ToArray();
    }
}
