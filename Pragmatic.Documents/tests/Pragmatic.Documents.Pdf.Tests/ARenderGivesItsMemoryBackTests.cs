using System.Diagnostics;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;
using Xunit.Abstractions;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
///     A PDF render gives its native memory back when it returns: a host that renders documents does not grow by
///     every document it has rendered (#275).
/// </summary>
/// <remarks>
///     <para>
///         Typst memoizes what it computes in process-wide caches, and kept every render's entries until
///         something evicted them; nothing did, so each 1,000-row report left about 195 MB behind. The managed
///         heap does not show it: the memory is the native library's.
///     </para>
///     <para>
///         Measured on the process's private bytes after a managed collection: they see native memory, and unlike
///         the working set the operating system does not shrink them by trimming pages, which under a loaded gate
///         made one render's growth come out negative. Kept out of the parallel run, so other tests' renders do
///         not land between two readings.
///     </para>
///     <para>
///         Three renders first, so fonts, first-use caches and the allocator's arenas have settled: on Linux,
///         glibc's allocator keeps the arenas a render grew, so the first renders of a fixed library still raise
///         private bytes (217 → 254 → 301 → 306 MB) and then stop, with an occasional step of one arena (306 →
///         342). A bound relative to the first render was tried and is not stable: that render's own growth
///         depends on what ran before it in the process.
///     </para>
///     <para>
///         Then seven renders of different documents, each measured on its own, and the assertion is on the
///         median of the seven. The two cases differ in shape, not only in size: the leak keeps about 30 MB on
///         Windows and 36 MB on Linux on <em>every</em> render, while an arena step is about the same size but
///         lands on one render now and then. A bound on the total over six renders sat between one step and the
///         leak, and two steps on a loaded runner crossed it with nothing leaking (#432). The median ignores up
///         to three steps among seven renders and still sees a leak on all of them.
///     </para>
/// </remarks>
[Collection(nameof(ARenderGivesItsMemoryBackTests))]
[CollectionDefinition(nameof(ARenderGivesItsMemoryBackTests), DisableParallelization = true)]
public class ARenderGivesItsMemoryBackTests(ITestOutputHelper output)
{
    private const int Lines = 3_000;

    private const int WarmUpRenders = 3;

    // Odd, so the median is one of the readings.
    private const int MeasuredRenders = 7;

    // With the eviction taken out, the median was 31.7 MB on Windows (each render 28.6 to 32.2 MB) and 27.5 to
    // 34.3 MB on Linux (each render 8.7 to 53.2 MB: glibc hands the leak back in uneven steps). With it, the
    // median was 0.2 MB on Windows and at most 61 KB on Linux, over a run that took a 34.6 MB arena step.
    private const long MedianBound = 10 * 1024 * 1024;

    [NativeRequiredFact]
    public void RepeatedRenders_DoNotKeepTheirNativeMemory()
    {
        for (var i = 0; i < WarmUpRenders; i++)
            PdfRenderer.Render(Report(i));

        var kept = new long[MeasuredRenders];
        var before = PrivateBytesAfterCollection();
        for (var i = 0; i < MeasuredRenders; i++)
        {
            PdfRenderer.Render(Report(WarmUpRenders + i));
            var after = PrivateBytesAfterCollection();
            kept[i] = after - before;
            before = after;
        }

        var median = kept.Order().ElementAt(MeasuredRenders / 2);
        var readings = $"each render of a {Lines:N0}-line document kept "
            + $"[{string.Join(", ", kept.Select(k => k.ToString("N0")))}] bytes, a median of {median:N0}";

        // Written when the test passes too: the margin on a runner is what a change of bound is measured by.
        output.WriteLine(readings);

        median.Should().BeLessThan(MedianBound,
            $"{readings}; a render that gives its memory back keeps nothing but an occasional allocator arena, "
            + "and one that does not keeps about 30 MB every time");
    }

    // A different document each time: memoized entries are keyed by content, so the same document twice could
    // reuse the first one's and hide the growth.
    private static DocumentModel Report(int run) => new DocumentBuilder()
        .Title($"Report {run}")
        .Page(page =>
        {
            for (var i = 0; i < Lines; i++)
                page.Text($"Run {run}, line {i}: a shipped item with its quantity, price and date.");
        })
        .Build();

    private static long PrivateBytesAfterCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();

        return process.PrivateMemorySize64;
    }
}
