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
///         Measured on the working set, the one number that sees native memory, after a managed collection. The
///         first render's growth is the footprint of one render; four more renders of different documents must
///         together grow by less than that one. Kept out of the parallel run, so other tests' renders do not
///         land between two readings.
///     </para>
/// </remarks>
[Collection(nameof(ARenderGivesItsMemoryBackTests))]
[CollectionDefinition(nameof(ARenderGivesItsMemoryBackTests), DisableParallelization = true)]
public class ARenderGivesItsMemoryBackTests
{
    private const int Lines = 1_500;

    [NativeRequiredFact]
    public void RepeatedRenders_DoNotKeepTheirNativeMemory()
    {
        var start = WorkingSetAfterCollection();
        PdfRenderer.Render(Report(0));
        var afterOne = WorkingSetAfterCollection();

        for (var i = 1; i <= 4; i++)
            PdfRenderer.Render(Report(i));
        var afterFive = WorkingSetAfterCollection();

        var oneRender = afterOne - start;
        (afterFive - afterOne).Should().BeLessThan(oneRender,
            $"four more renders kept {afterFive - afterOne:N0} bytes, against {oneRender:N0} for the first one; "
            + "a render that gives its memory back keeps none of it");
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

    private static long WorkingSetAfterCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();

        return process.WorkingSet64;
    }
}
