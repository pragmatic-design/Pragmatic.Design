using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Samples.Samples;

/// <summary>
///     Using the <see cref="IEmailRenderer"/> abstraction directly instead of
///     newing up <see cref="EmailHtmlRenderer"/> inline. Depending on the
///     interface lets you inject/swap the renderer (testing, multi-format output,
///     a custom HTML strategy). This sample exercises BOTH interface members:
///     <c>Render</c> (to string) and <c>RenderTo</c> (streaming to a TextWriter).
/// </summary>
public static class RendererInterfaceSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- IEmailRenderer abstraction (Render + RenderTo) ---");

        var model = new EmailBuilder()
            .Subject("Your weekly summary")
            .Preheader("Here is what happened in your account this week")
            .Section(s => s.Column(c => c
                .Heading("Weekly summary", level: 1)
                .Text("Here is what happened in your account this week.")
                .Spacer()
                .Button("View dashboard", "https://example.com/dashboard",
                    backgroundColor: "#0A66C2")))
            .Build();

        // Depend on the interface, not the concrete renderer.
        IEmailRenderer renderer = new EmailHtmlRenderer();

        // 1) Render to a string.
        var html = renderer.Render(model);
        var stringPath = Path.Combine(outputDir, "via-interface.html");
        File.WriteAllText(stringPath, html);
        Console.WriteLine($"  Render (string)        {html.Length} chars -> {Path.GetFileName(stringPath)}");

        // 2) Stream straight to a TextWriter (e.g. an HTTP response or a file).
        var streamPath = Path.Combine(outputDir, "via-interface-streamed.html");
        using (var writer = new StreamWriter(streamPath))
        {
            renderer.RenderTo(writer, model);
        }
        Console.WriteLine($"  RenderTo (streaming)   -> {Path.GetFileName(streamPath)}");
        Console.WriteLine();
    }
}
