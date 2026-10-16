using System.Diagnostics;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

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
///         342). Then six renders of different documents, measured. With the leak, each kept about 30 MB on
///         Windows and 36 MB on Linux, so six keep over 180 MB; without it, six keep at most one arena step on
///         Linux and about 2.5 MB on Windows. 64 MB is far from both. A bound relative to the first render was
///         tried and is not stable: that render's own growth depends on what ran before it in the process.
///     </para>
/// </remarks>
[Collection(nameof(ARenderGivesItsMemoryBackTests))]
[CollectionDefinition(nameof(ARenderGivesItsMemoryBackTests), DisableParallelization = true)]
public class ARenderGivesItsMemoryBackTests
{
    private const int Lines = 3_000;

    private const int WarmUpRenders = 3;

    private const int MeasuredRenders = 6;

    private const long Bound = 64 * 1024 * 1024;

    [NativeRequiredFact]
    public void RepeatedRenders_DoNotKeepTheirNativeMemory()
    {
        for (var i = 0; i < WarmUpRenders; i++)
            PdfRenderer.Render(Report(i));
        var settled = PrivateBytesAfterCollection();

        for (var i = WarmUpRenders; i < WarmUpRenders + MeasuredRenders; i++)
            PdfRenderer.Render(Report(i));
        var kept = PrivateBytesAfterCollection() - settled;

        kept.Should().BeLessThan(Bound,
            $"{MeasuredRenders} renders of {Lines:N0}-line documents kept {kept:N0} bytes; a render that gives its "
            + "memory back keeps at most an allocator arena, and one that does not keeps about 30 MB each");
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
