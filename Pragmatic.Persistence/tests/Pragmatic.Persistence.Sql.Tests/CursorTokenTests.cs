using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Cursor tokens: a cursor's values signed into a Base64Url string and read back as the same values; a token altered,
///     signed elsewhere, or of another shape is refused with the reason.
/// </summary>
public class CursorTokenTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Round_trips_every_value_type_in_a_url_safe_token()
    {
        var signer = new SqlCursorSigner(Key, "orders");
        var at = new DateTimeOffset(2026, 10, 19, 8, 30, 15, TimeSpan.FromHours(2)).AddTicks(1234567);
        var id = Guid.NewGuid();

        var two = signer.Encode((at, id));
        Assert.DoesNotContain(two, c => c is '+' or '/' or '=');
        Assert.Equal((at, id), signer.Decode<DateTimeOffset, Guid>(two));

        Assert.Equal(id, signer.Decode<Guid>(signer.Encode(id)));
        Assert.Equal("ORD-0042", signer.Decode<string>(signer.Encode("ORD-0042")));
        Assert.Equal((1234.56m, 42L, true), signer.Decode<decimal, long, bool>(signer.Encode((1234.56m, 42L, true))));
        var four = signer.Encode((new DateOnly(2026, 10, 19), new TimeOnly(8, 30), 1.5, new byte[] { 1, 2, 3 }));
        var (day, time, number, bytes) = signer.Decode<DateOnly, TimeOnly, double, byte[]>(four);
        Assert.Equal((new DateOnly(2026, 10, 19), new TimeOnly(8, 30), 1.5), (day, time, number));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
        var local = new DateTime(2026, 10, 19, 8, 30, 0, DateTimeKind.Utc);
        Assert.Equal((local, (short)7, (byte)9, 2.5f), signer.Decode<DateTime, short, byte, float>(signer.Encode((local, (short)7, (byte)9, 2.5f))));

        // An enum by its number, a null in a nullable key.
        Assert.Equal((OrderStatus.Paid, (int?)null), signer.Decode<OrderStatus, int?>(signer.Encode((OrderStatus.Paid, (int?)null))));
        Assert.Null(signer.Decode<string?>(signer.Encode<string?>(null)));
    }

    [Fact]
    public void Refuses_a_token_that_is_not_what_it_claims()
    {
        var signer = new SqlCursorSigner(Key, "orders");
        var token = signer.Encode((new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid()));

        // Altered: one character of the payload changed; the signature no longer matches.
        var altered = (token[8] == 'A' ? 'B' : 'A') + token[1..];
        var tampered = Assert.Throws<SqlCursorException>(() => signer.Decode<DateTimeOffset, Guid>(altered[..8] + altered[..1] + altered[9..]));
        Assert.Contains("signature", tampered.Message, StringComparison.Ordinal);

        // Another key, another purpose: not issued here.
        Assert.Throws<SqlCursorException>(() => new SqlCursorSigner(new byte[32], "orders").Decode<DateTimeOffset, Guid>(token));
        Assert.Throws<SqlCursorException>(() => new SqlCursorSigner(Key, "customers").Decode<DateTimeOffset, Guid>(token));

        // Another shape: two values read as one, or as the wrong types; a null where the type takes none.
        Assert.Contains("2 values, 1 expected", Assert.Throws<SqlCursorException>(() => signer.Decode<Guid>(token)).Message, StringComparison.Ordinal);
        Assert.Contains("Value 1", Assert.Throws<SqlCursorException>(() => signer.Decode<Guid, DateTimeOffset>(token)).Message, StringComparison.Ordinal);
        Assert.Contains("null", Assert.Throws<SqlCursorException>(() => signer.Decode<int>(signer.Encode<int?>(null))).Message, StringComparison.Ordinal);

        // Not a token at all, or too short; a key too short to sign with.
        Assert.Throws<SqlCursorException>(() => signer.Decode<Guid>("not a token!"));
        Assert.Throws<SqlCursorException>(() => signer.Decode<Guid>("AQE"));
        Assert.Throws<ArgumentException>(() => new SqlCursorSigner(new byte[8]));
    }
}
