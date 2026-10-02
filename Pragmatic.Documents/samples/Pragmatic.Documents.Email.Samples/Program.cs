using Pragmatic.Documents.Email.Samples.Samples;

Console.WriteLine("=== Pragmatic.Documents.Email Samples ===\n");

// The recommended way first: a .pdxemail file through IPdxTemplates (MarkupEmailSample) gives the
// subject, the HTML and the plain text in the recipient's language. The rest build the model in code:
//   Pragmatic.Email.Model       — EmailBuilder -> EmailModel
//   Pragmatic.Email.Templates   — PDX markup -> EmailTemplate + resolver
//   Pragmatic.Documents.Email   — EmailHtmlRenderer -> HTML string
//
// Output files land under %TEMP%/pragmatic-email-sample/ as .html so you
// can open them in a browser and inspect the rendered email.

var outputDir = Path.Combine(Path.GetTempPath(), "pragmatic-email-sample");
Directory.CreateDirectory(outputDir);
Console.WriteLine($"Output directory: {outputDir}\n");

await MarkupEmailSample.Run(outputDir);
SimpleEmailSample.Run(outputDir);
HeroAndColumnsSample.Run(outputDir);
FooterAndSocialSample.Run(outputDir);
RendererInterfaceSample.Run(outputDir);

Console.WriteLine("\n=== All samples completed. Open the .html files in a browser. ===");
