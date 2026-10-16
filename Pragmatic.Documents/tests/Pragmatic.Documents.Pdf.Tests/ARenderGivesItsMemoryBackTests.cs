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
///         made one render's growth come out negative. One render first, so fonts and first-use caches are
///         loaded, then four renders of different documents. Kept out of the parallel run, so other tests' renders
///         do not land between two readings.
///     </para>
///     <para>
///         The bound is absolute because a relative one is not stable: the first render's own growth depends on
///         what ran before it in the process. With the leak, each of these documents kept about 30 MB, so four
///         kept over 100 MB; without it, four keep about 2.5 MB. 16 MB is far from both.
///     </para>
/// </remarks>
[Collection(nameof(ARenderGivesItsMemoryBackTests))]
[CollectionDefinition(nameof(ARenderGivesItsMemoryBackTests), DisableParallelization = true)]
public class ARenderGivesItsMemoryBackTests
{
    private const int Lines = 3_000;

    private const long Bound = 16 * 1024 * 1024;

    [NativeRequiredFact]
    public void RepeatedRenders_DoNotKeepTheirNativeMemory()
    {
        PdfRenderer.Render(Report(0));
        var warm = PrivateBytesAfterCollection();

        for (var i = 1; i <= 4; i++)
            PdfRenderer.Render(Report(i));
        var kept = PrivateBytesAfterCollection() - warm;

        kept.Should().BeLessThan(Bound,
            $"four renders of {Lines:N0}-line documents kept {kept:N0} bytes; a render that gives its memory back "
            + "keeps none of it, and one that does not keeps about 30 MB each");
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
