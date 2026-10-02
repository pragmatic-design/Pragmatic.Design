using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Tests;

public class PdfBatchTests
{
    private static DocumentModel CreateModel(string title, int pages = 1)
    {
        var builder = new DocumentBuilder().Title(title);
        for (var i = 1; i <= pages; i++)
            builder.Page(p => p.Heading($"{title} - Page {i}"));
        return builder.Build();
    }

    [NativeRequiredFact]
    public async Task RenderAsync_MultipleModels_ProducesAllPdfs()
    {
        var models = new[]
        {
            CreateModel("Batch1", 2),
            CreateModel("Batch2", 1),
            CreateModel("Batch3", 3),
        };

        var pdfs = await PdfBatch.RenderAsync(models);

        pdfs.Should().HaveCount(3);
        foreach (var pdf in pdfs)
        {
            System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
        }

        PdfOperations.GetPageCount(pdfs[0]).Should().Be(2);
        PdfOperations.GetPageCount(pdfs[1]).Should().Be(1);
        PdfOperations.GetPageCount(pdfs[2]).Should().Be(3);
    }

    [NativeRequiredFact]
    public async Task RenderAndMergeAsync_CombinesAll()
    {
        var models = new[]
        {
            CreateModel("Merge1", 2),
            CreateModel("Merge2", 3),
        };

        var merged = await PdfBatch.RenderAndMergeAsync(models);

        merged.Should().NotBeEmpty();
        PdfOperations.GetPageCount(merged).Should().Be(5);
    }

    [NativeRequiredFact]
    public async Task RenderAsync_Empty_ReturnsEmpty()
    {
        var pdfs = await PdfBatch.RenderAsync(Array.Empty<DocumentModel>());

        pdfs.Should().BeEmpty();
    }

    [NativeRequiredFact]
    public async Task RenderAsync_MaxConcurrency1_StillProcessesAll()
    {
        var models = new[]
        {
            CreateModel("Serial1"),
            CreateModel("Serial2"),
            CreateModel("Serial3"),
        };

        var pdfs = await PdfBatch.RenderAsync(models, maxConcurrency: 1);

        pdfs.Should().HaveCount(3);
    }

    [NativeRequiredFact]
    public async Task RenderAndMergeAsync_SingleModel_WorksCorrectly()
    {
        var models = new[] { CreateModel("Single", 2) };

        var merged = await PdfBatch.RenderAndMergeAsync(models);

        PdfOperations.GetPageCount(merged).Should().Be(2);
    }
}
