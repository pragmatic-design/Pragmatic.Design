using System.Diagnostics;
using System.Globalization;
using Pragmatic.Documents.Benchmarks.Workload;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Spreadsheet;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Xlsx;

namespace Pragmatic.Documents.Benchmarks;

/// <summary>
///     How much memory one render holds at its peak, as the report grows: what the allocation ratchet cannot see,
///     since it counts bytes allocated per call and not bytes alive at once (#144).
/// </summary>
/// <remarks>
///     <para>
///         <c>memory</c> runs every format and size, each in a process of its own (<c>memory-one</c>), so no
///         measure inherits another's heap. The child renders a 100-row report first, so JIT and first-use
///         caches are paid before the measure, collects fully, then renders the measured one while a thread
///         samples the managed heap every millisecond. It reports the peak sample above the heap before the
///         render, the process's peak working set, and the size of what was written.
///     </para>
///     <para>
///         A sample is a lower bound of the true peak: a spike shorter than a millisecond can fall between two.
///         The slope between sizes is the number to read, more than any one figure.
///     </para>
/// </remarks>
public static class PeakMemory
{
    private static readonly int[] Sizes = [1_000, 10_000, 50_000];
    private static readonly string[] Formats = ["pdf", "xlsx"];

    /// <summary>Runs each format at each size in a child process and prints one table.</summary>
    public static void RunAll()
    {
        var self = Environment.ProcessPath
            ?? throw new InvalidOperationException("The process path is unknown, so the measure cannot start a child.");

        Console.WriteLine("| Format | Rows | Output bytes | Managed heap peak above start | Process peak working set |");
        Console.WriteLine("|---|---:|---:|---:|---:|");
        foreach (var format in Formats)
        {
            foreach (var rows in Sizes)
            {
                var child = Process.Start(new ProcessStartInfo(self)
                {
                    ArgumentList = { "memory-one", format, rows.ToString(CultureInfo.InvariantCulture) },
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                }) ?? throw new InvalidOperationException($"The {format} {rows} measure did not start.");

                var line = child.StandardOutput.ReadToEnd().Trim();
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException($"The {format} {rows} measure failed: {line}");

                Console.WriteLine(line);
            }
        }
    }

    /// <summary>Measures one render and prints its table row.</summary>
    public static void RunOne(string format, int rows)
    {
        Render(format, 100);
        FullCollect();

        var before = GC.GetTotalMemory(forceFullCollection: false);
        var peak = before;
        using var stop = new CancellationTokenSource();
        var sampler = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
                Thread.Sleep(1);
            }
        }) { IsBackground = true };

        sampler.Start();
        var written = Render(format, rows);
        peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
        stop.Cancel();
        sampler.Join();

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {format} | {rows:N0} | {written:N0} | {peak - before:N0} | {process.PeakWorkingSet64:N0} |"));
    }

    private static long Render(string format, int rows) => format switch
    {
        "pdf" => RenderPdf(rows),
        "xlsx" => RenderXlsx(rows),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "pdf or xlsx"),
    };

    private static long RenderPdf(int rows)
    {
        var template = PdxDocParser.Parse(Workloads.ReportTemplate);
        var data = new TemplateDataContext().AddSource("report", Workloads.Typed(rows));
        var model = new DocumentTemplateResolver().ResolveAsync(template, data).AsTask().GetAwaiter().GetResult();

        return PdfRenderer.Render(model).LongLength;
    }

    private static long RenderXlsx(int rows)
    {
        var lines = Workloads.Typed(rows).Lines;
        var book = new SpreadsheetBuilder()
            .Sheet("Lines", sheet =>
            {
                sheet.HeaderRow(Workloads.Headers);
                foreach (var l in lines)
                    sheet.Row(l.Number, l.Sku, l.Description, l.Category, l.Quantity, l.UnitPrice, l.Discount,
                        l.VatRate, l.Net, l.ShippedOn);
            })
            .Build();

        using var output = new MemoryStream();
        XlsxRenderer.RenderTo(output, book);

        return output.Length;
    }

    private static void FullCollect()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
