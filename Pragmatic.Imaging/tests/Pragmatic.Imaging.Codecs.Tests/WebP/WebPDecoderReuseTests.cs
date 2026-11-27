using Pragmatic.Imaging.Codecs.WebP;

namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>
///     The WebP decoders keep their working state on the thread between decodes, so a decode allocates only the
///     image it returns. What a file leaves in that state (coefficient probabilities, segment and filter
///     settings, partitions, transforms, prefix codes) must not reach the next file: each file decoded right
///     after every other one, of either kind and any size, is the image a thread that never decoded reads.
/// </summary>
public sealed class WebPDecoderReuseTests
{
    public static TheoryData<string> Files() => WebPFiles.All();

    [Theory]
    [MemberData(nameof(Files))]
    public void Decode_RightAfterEachOtherFileOnTheSameThread_IsWhatAFreshThreadReads(string file)
    {
        var encoded = WebPFiles.Read(file);
        var expected = OnAFreshThread(() => DecodeToArray(encoded));
        var others = Directory.EnumerateFiles(WebPFiles.Directory, "*.webp").Select(Path.GetFileName).Where(name => name != file).Order(StringComparer.Ordinal).ToArray();

        var mismatches = OnAFreshThread(() =>
        {
            var found = new List<string>();
            foreach (var other in others)
            {
                using (WebPDecoder.Decode(WebPFiles.Read(other!)))
                {
                }

                if (!DecodeToArray(encoded).AsSpan().SequenceEqual(expected))
                    found.Add(other!);
            }

            return found;
        });

        Assert.True(mismatches.Count == 0, $"{file} decoded differently right after: {string.Join(", ", mismatches)}");
    }

    private static byte[] DecodeToArray(byte[] encoded)
    {
        using var decoded = WebPDecoder.Decode(encoded);
        return decoded.Pixels.ToArray();
    }

    private static T OnAFreshThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("The decode on its own thread failed.", failure);
        return result;
    }
}
