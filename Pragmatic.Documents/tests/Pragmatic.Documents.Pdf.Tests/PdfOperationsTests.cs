using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Tests;

public class PdfOperationsTests
{
    private static byte[] CreateSimplePdf(string title, int pages = 1)
    {
        var builder = new DocumentBuilder().Title(title);
        for (var i = 1; i <= pages; i++)
            builder.Page(p => p.Heading($"{title} - Page {i}"));
        return PdfRenderer.Render(builder.Build());
    }

    [NativeRequiredFact]
    public void GetPageCount_SinglePage_Returns1()
    {
        var pdf = CreateSimplePdf("Single");

        var count = PdfOperations.GetPageCount(pdf);

        count.Should().Be(1);
    }

    [NativeRequiredFact]
    public void GetPageCount_MultiPage_ReturnsCorrectCount()
    {
        var pdf = CreateSimplePdf("Multi", 5);

        var count = PdfOperations.GetPageCount(pdf);

        count.Should().Be(5);
    }

    [NativeRequiredFact]
    public void Merge_TwoPdfs_ProducesValidPdf()
    {
        var pdf1 = CreateSimplePdf("Doc1", 2);
        var pdf2 = CreateSimplePdf("Doc2", 3);

        var merged = PdfOperations.Merge(pdf1, pdf2);

        merged.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(merged[..4]).Should().Be("%PDF");

        var pageCount = PdfOperations.GetPageCount(merged);
        pageCount.Should().Be(5); // 2 + 3
    }

    [NativeRequiredFact]
    public void Merge_SinglePdf_ReturnsSamePdf()
    {
        var pdf = CreateSimplePdf("Solo");

        var merged = PdfOperations.Merge(pdf);

        merged.Should().BeEquivalentTo(pdf);
    }

    [NativeRequiredFact]
    public void Merge_ThreePdfs_CombinesAll()
    {
        var pdf1 = CreateSimplePdf("A", 1);
        var pdf2 = CreateSimplePdf("B", 2);
        var pdf3 = CreateSimplePdf("C", 3);

        var merged = PdfOperations.Merge(pdf1, pdf2, pdf3);

        PdfOperations.GetPageCount(merged).Should().Be(6); // 1 + 2 + 3
    }

    [NativeRequiredFact]
    public async Task MergeAsync_TwoPdfs_ProducesValidPdf()
    {
        var pdf1 = CreateSimplePdf("Async1", 2);
        var pdf2 = CreateSimplePdf("Async2", 3);

        var merged = await PdfOperations.MergeAsync([pdf1, pdf2]);

        PdfOperations.GetPageCount(merged).Should().Be(5);
    }

    [NativeRequiredFact]
    public void Split_ExtractsPageRange()
    {
        var pdf = CreateSimplePdf("Split", 5);

        var extracted = PdfOperations.Split(pdf, fromPage: 2, toPage: 4);

        extracted.Should().NotBeEmpty();
        PdfOperations.GetPageCount(extracted).Should().Be(3);
    }

    [NativeRequiredFact]
    public void Split_SinglePage_ExtractsOnePage()
    {
        var pdf = CreateSimplePdf("SplitOne", 5);

        var extracted = PdfOperations.Split(pdf, fromPage: 3, toPage: 3);

        PdfOperations.GetPageCount(extracted).Should().Be(1);
    }

    [NativeRequiredFact]
    public void Split_AllPages_ReturnsCopy()
    {
        var pdf = CreateSimplePdf("SplitAll", 3);

        var extracted = PdfOperations.Split(pdf, fromPage: 1, toPage: 3);

        PdfOperations.GetPageCount(extracted).Should().Be(3);
    }

    [NativeRequiredFact]
    public void Split_InvalidRange_Throws()
    {
        var pdf = CreateSimplePdf("SplitBad", 3);

        var act = () => PdfOperations.Split(pdf, fromPage: 0, toPage: 3);

        act.Should().Throw<PdfRenderException>();
    }

    [NativeRequiredFact]
    public void Split_RangeExceedsPages_Throws()
    {
        var pdf = CreateSimplePdf("SplitOver", 3);

        var act = () => PdfOperations.Split(pdf, fromPage: 2, toPage: 5);

        act.Should().Throw<PdfRenderException>();
    }

    [NativeRequiredFact]
    public async Task SplitAsync_ExtractsRange()
    {
        var pdf = CreateSimplePdf("AsyncSplit", 5);

        var extracted = await PdfOperations.SplitAsync(pdf, fromPage: 1, toPage: 2);

        PdfOperations.GetPageCount(extracted).Should().Be(2);
    }

    [NativeRequiredFact]
    public void Merge_Empty_Throws()
    {
        var act = () => PdfOperations.Merge(ReadOnlySpan<byte[]>.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
