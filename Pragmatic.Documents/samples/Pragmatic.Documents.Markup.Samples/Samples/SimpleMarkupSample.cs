using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Documents.Pdf;
using Pragmatic.Documents.Templating.Data;

namespace Pragmatic.Documents.Markup.Samples.Samples;

/// <summary>
///     The smallest template: <c>templates/hello.pdxdoc</c>, static text, found by name and rendered.
/// </summary>
public static class SimpleMarkupSample
{
    public static async Task Run(IServiceProvider services, string outputDir)
    {
        Console.WriteLine("--- Simple template (templates/hello.pdxdoc, no expressions) ---");

        await using var scope = services.CreateAsyncScope();
        var templates = scope.ServiceProvider.GetRequiredService<IPdxTemplates>();

        var document = await templates.DocumentAsync("hello.pdxdoc", "en-US", new TemplateDataContext());
        var bytes = PdfRenderer.Render(document.Model);

        var path = Path.Combine(outputDir, "hello.pdf");
        await File.WriteAllBytesAsync(path, bytes);
        Console.WriteLine($"  hello.pdf              {bytes.Length} bytes");
        Console.WriteLine();
    }
}
