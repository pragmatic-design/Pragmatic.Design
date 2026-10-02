using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Identifiers;

namespace Pragmatic.Persistence.Tests.Identifiers;

public class Guid7Tests
{
    [Fact]
    public void New_ReturnsValidGuid7()
    {
        var guid = Guid7.New();

        Guid7.IsVersion7(guid).Should().BeTrue();
    }

    [Fact]
    public void New_GeneratesUniqueValues()
    {
        var guids = Enumerable.Range(0, 1000)
            .Select(_ => Guid7.New())
            .ToHashSet();

        guids.Should().HaveCount(1000);
    }

    [Fact]
    public void New_IsChronologicallyOrdered()
    {
        var guids = Enumerable.Range(0, 100)
            .Select(_ => Guid7.New())
            .ToList();

        var timestamps = guids
            .Select(g => Guid7.GetTimestamp(g)!.Value)
            .ToList();

        timestamps.Should().BeInAscendingOrder();
    }

    [Fact]
    public void NewForSqlServer_ReturnsNonEmptyGuid()
    {
        var guid = Guid7.NewForSqlServer();

        guid.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void NewForSqlServer_MultipleCallsAreUnique()
    {
        var guids = Enumerable.Range(0, 1000)
            .Select(_ => Guid7.NewForSqlServer())
            .ToHashSet();

        guids.Should().HaveCount(1000);
    }

    [Fact]
    public void NewForSqlServer_MaintainsChronologicalOrder()
    {
        // SQL Server sorts bytes 10-15 first, so those should be ordered
        var guids = Enumerable.Range(0, 50)
            .Select(_ => Guid7.NewForSqlServer())
            .ToList();

        var lastSixBytes = guids.Select(g =>
        {
            Span<byte> bytes = stackalloc byte[16];
            g.TryWriteBytes(bytes, bigEndian: true, out _);
            return new byte[] { bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15] };
        }).ToList();

        // Bytes 10-15 should be non-decreasing (timestamps move there)
        for (var i = 1; i < lastSixBytes.Count; i++)
        {
            var comparison = CompareByteArrays(lastSixBytes[i - 1], lastSixBytes[i]);
            comparison.Should().BeLessThanOrEqualTo(0,
                $"bytes 10-15 at index {i} should be >= index {i - 1}");
        }
    }

    [Fact]
    public void FromTimestamp_EmbeddsCorrectTimestamp()
    {
        var expected = DateTimeOffset.UtcNow;

        var guid = Guid7.FromTimestamp(expected);
        var actual = Guid7.GetTimestamp(guid);

        actual.Should().NotBeNull();
        actual!.Value.Should().BeCloseTo(expected, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void FromTimestamp_WithEpoch_ProducesValidGuid7()
    {
        var epoch = DateTimeOffset.UnixEpoch;

        var guid = Guid7.FromTimestamp(epoch);

        Guid7.IsVersion7(guid).Should().BeTrue();
    }

    [Fact]
    public void FromTimestamp_WithFutureTimestamp_Works()
    {
        var future = new DateTimeOffset(2050, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var guid = Guid7.FromTimestamp(future);
        var actual = Guid7.GetTimestamp(guid);

        actual.Should().NotBeNull();
        actual!.Value.Should().BeCloseTo(future, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void GetTimestamp_WithGuid7_ReturnsCorrectTimestamp()
    {
        var before = DateTimeOffset.UtcNow;
        var guid = Guid7.New();
        var after = DateTimeOffset.UtcNow;

        var timestamp = Guid7.GetTimestamp(guid);

        timestamp.Should().NotBeNull();
        timestamp!.Value.Should().BeOnOrAfter(before.AddMilliseconds(-1));
        timestamp.Value.Should().BeOnOrBefore(after.AddMilliseconds(1));
    }

    [Fact]
    public void GetTimestamp_WithNonGuid7_ReturnsNull()
    {
        var v4 = Guid.NewGuid();

        Guid7.GetTimestamp(v4).Should().BeNull();
    }

    [Fact]
    public void GetTimestamp_WithEmptyGuid_ReturnsNull()
    {
        Guid7.GetTimestamp(Guid.Empty).Should().BeNull();
    }

    [Fact]
    public void IsVersion7_WithGuid7_ReturnsTrue()
    {
        var guid = Guid7.New();

        Guid7.IsVersion7(guid).Should().BeTrue();
    }

    [Fact]
    public void IsVersion7_WithGuidV4_ReturnsFalse()
    {
        var guid = Guid.NewGuid();

        Guid7.IsVersion7(guid).Should().BeFalse();
    }

    [Fact]
    public void IsVersion7_WithEmptyGuid_ReturnsFalse()
    {
        Guid7.IsVersion7(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void ShuffleForSqlServer_MovesTimestampToEnd()
    {
        var guid = Guid7.New();
        Span<byte> originalBytes = stackalloc byte[16];
        guid.TryWriteBytes(originalBytes, bigEndian: true, out _);

        var shuffled = Guid7.ShuffleForSqlServer(guid);
        Span<byte> shuffledBytes = stackalloc byte[16];
        shuffled.TryWriteBytes(shuffledBytes, bigEndian: true, out _);

        // Original bytes 0-5 (timestamp) should be at shuffled bytes 10-15
        for (var i = 0; i < 6; i++)
            shuffledBytes[10 + i].Should().Be(originalBytes[i],
                $"timestamp byte {i} should move from position {i} to {10 + i}");
    }

    [Fact]
    public void ShuffleForSqlServer_PreservesVersionVariant()
    {
        var guid = Guid7.New();
        Span<byte> originalBytes = stackalloc byte[16];
        guid.TryWriteBytes(originalBytes, bigEndian: true, out _);

        var shuffled = Guid7.ShuffleForSqlServer(guid);
        Span<byte> shuffledBytes = stackalloc byte[16];
        shuffled.TryWriteBytes(shuffledBytes, bigEndian: true, out _);

        // Bytes 6-9 should be preserved
        shuffledBytes[6].Should().Be(originalBytes[6]);
        shuffledBytes[7].Should().Be(originalBytes[7]);
        shuffledBytes[8].Should().Be(originalBytes[8]);
        shuffledBytes[9].Should().Be(originalBytes[9]);
    }

    [Fact]
    public void ShuffleForSqlServer_DifferentFromOriginal()
    {
        var guid = Guid7.New();

        var shuffled = Guid7.ShuffleForSqlServer(guid);

        shuffled.Should().NotBe(guid);
    }

    [Fact]
    public void GetTimestamp_OnSqlServerOptimizedGuid_DoesNotThrow()
    {
        // The SQL-optimized layout shuffles random bytes into the timestamp position, so the
        // 48-bit value can exceed the DateTimeOffset range. GetTimestamp must guard rather than throw
        // an ArgumentOutOfRangeException (unguarded, ~10% of random 48-bit values are out of range).
        for (var i = 0; i < 500; i++)
        {
            var g = Guid7.NewForSqlServer();
            var act = () => Guid7.GetTimestamp(g);
            act.Should().NotThrow();
        }
    }

    private static int CompareByteArrays(byte[] a, byte[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] < b[i])
                return -1;
            if (a[i] > b[i])
                return 1;
        }

        return 0;
    }
}
