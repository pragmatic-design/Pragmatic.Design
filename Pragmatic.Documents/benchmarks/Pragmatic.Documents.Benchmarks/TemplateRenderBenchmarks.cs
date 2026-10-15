using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Pragmatic.Documents.Benchmarks.Workload;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Benchmarks;

/// <summary>
///     A <c>.pdxdoc</c> report with a 1,000-row table: resolved against its data, and rendered to PDF.
/// </summary>
/// <remarks>
///     <para>
///         <c>DataAccess</c> isolates what the template's data costs to read: the same template resolved into
///         a document model, through the accessors the generator writes for a source added with its type known,
///         and through the reflective fallback for one added as <see cref="object" />. Ten fields of a thousand
///         rows are ten thousand reads either way. <c>FieldReads</c> takes the document away: the same ten
///         thousand reads, path by path, through the data context alone.
///     </para>
///     <para>
///         <c>Pdf</c> is the whole path a caller pays for: template and data to PDF bytes. It has no competitor
///         row: QuestPDF's licence forbids using it to develop or market a competing PDF component, which a
///         committed comparison would do (#143).
///     </para>
///     <para>
///         <see cref="Setup" /> checks both data paths resolve the same document, every row of it, with no
///         warning, before anything is timed.
///     </para>
/// </remarks>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class TemplateRenderBenchmarks
{
    private readonly DocumentTemplateResolver _resolver = new();
    private DocumentTemplate _template = null!;
    private InvoiceReport _typed = null!;
    private ReflectedInvoiceReport _reflected = null!;

    [GlobalSetup]
    public void Setup()
    {
        _template = PdxDocParser.Parse(Workloads.ReportTemplate);
        _typed = Workloads.Typed();
        _reflected = Workloads.Reflected();

        VerifyBothPathsResolveTheSameDocument();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("DataAccess")]
    public ValueTask<DocumentModel> Resolve_GeneratedAccessors()
        => _resolver.ResolveAsync(_template, TypedData());

    [Benchmark, BenchmarkCategory("DataAccess")]
    public ValueTask<DocumentModel> Resolve_Reflection()
        => _resolver.ResolveAsync(_template, ReflectedData());

    [Benchmark(Baseline = true), BenchmarkCategory("FieldReads")]
    public async Task<int> Read_GeneratedAccessors()
    {
        var data = TypedData();
        var found = 0;
        foreach (var line in _typed.Lines)
            found += await ReadEveryField(data.CreateChildScope("item", line)).ConfigureAwait(false);

        return found;
    }

    [Benchmark, BenchmarkCategory("FieldReads")]
    public async Task<int> Read_Reflection()
    {
        var data = ReflectedData();
        var found = 0;
        foreach (var line in _reflected.Lines)
            found += await ReadEveryField(data.CreateChildScope("item", line)).ConfigureAwait(false);

        return found;
    }

    [Benchmark, BenchmarkCategory("Pdf")]
    public async Task<byte[]> Pdf_TemplateToPdf()
        => PdfRenderer.Render(await _resolver.ResolveAsync(_template, TypedData()).ConfigureAwait(false));

    private TemplateDataContext TypedData() => new TemplateDataContext().AddSource("report", _typed);

    // The cast picks the untyped overload: the generic one would make the generator write accessors for
    // the twin as well, and both rows would measure them.
    private TemplateDataContext ReflectedData() => new TemplateDataContext().AddSource("report", (object)_reflected);

    private static readonly string[] FieldPaths =
    [
        "item.number", "item.sku", "item.description", "item.category", "item.quantity",
        "item.unitPrice", "item.discount", "item.vatRate", "item.net", "item.shippedOn",
    ];

    private static async ValueTask<int> ReadEveryField(TemplateDataContext row)
    {
        var found = 0;
        foreach (var path in FieldPaths)
        {
            if (await row.ResolveAsync(path).ConfigureAwait(false) is not null)
                found++;
        }

        return found;
    }

    private void VerifyBothPathsResolveTheSameDocument()
    {
        var typedData = TypedData();
        var reflectedData = ReflectedData();
        var typed = DocumentSerializer.Serialize(_resolver.ResolveAsync(_template, typedData).AsTask().GetAwaiter().GetResult());
        var reflected = DocumentSerializer.Serialize(_resolver.ResolveAsync(_template, reflectedData).AsTask().GetAwaiter().GetResult());

        var failures = new List<string>();
        if (typedData.Warnings.Count > 0)
            failures.Add($"the typed path warned: {typedData.Warnings[0].Message}");
        if (reflectedData.Warnings.Count > 0)
            failures.Add($"the reflective path warned: {reflectedData.Warnings[0].Message}");
        if (typed != reflected)
            failures.Add("the two paths resolved different documents");
        if (!typed.Contains($"SKU-{Workloads.Rows:D5}", StringComparison.Ordinal))
            failures.Add("the last row is missing");

        const int everyField = Workloads.Rows * Workloads.Fields;
        var readTyped = Read_GeneratedAccessors().GetAwaiter().GetResult();
        var readReflected = Read_Reflection().GetAwaiter().GetResult();
        if (readTyped != everyField || readReflected != everyField)
            failures.Add($"the field reads found {readTyped} and {readReflected} of {everyField} values");

        var pdf = Pdf_TemplateToPdf().GetAwaiter().GetResult();
        if (pdf.Length < 1024 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P')
            failures.Add($"the PDF is not one ({pdf.Length} bytes)");

        if (failures.Count > 0)
            throw new InvalidOperationException("The rows would measure something else: " + string.Join("; ", failures));
    }
}
