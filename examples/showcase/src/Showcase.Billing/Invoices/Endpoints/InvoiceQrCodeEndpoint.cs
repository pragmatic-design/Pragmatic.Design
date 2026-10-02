using Pragmatic.Endpoints.Responses;
using Pragmatic.Imaging;

namespace Showcase.Billing.Endpoints;

/// <summary>
/// Renders a QR code PNG for a payload (e.g. an invoice pay-link).
/// Demonstrates: Pragmatic.Imaging integration end-to-end — the full Rust native pipeline exercised
/// over HTTP: <see cref="QrCode.GeneratePng(string, uint, uint)"/> (native encode) →
/// <see cref="ImagePipeline.Load(byte[], ImagingOptions)"/> (native decode) →
/// <see cref="ImagePipeline.Thumbnail"/> (native resize) → <see cref="ImagePipeline.Encode"/> (native encode),
/// streamed back via the endpoint <see cref="FileResponse"/> contract.
/// </summary>
[Endpoint(HttpVerb.Get, "api/invoices/qr.png")]
[RequirePermission(BillingPermissions.Invoice.Read)]
[ApiSummary("Invoice QR Code (PNG)")]
[ApiDescription("Generates a QR code PNG for the given payload, sized via the imaging pipeline.")]
[ApiTags("Invoices")]
public partial class InvoiceQrCodeEndpoint : Endpoint<FileResponse>
{
    private const string PngContentType = "image/png";

    /// <summary>Payload to encode (e.g. an invoice pay-link or number). Defaults to a sample link.</summary>
    [FromQuery]
    public string? Text { get; set; }

    /// <summary>Target square size in pixels for the rendered QR (bounded 64–2048). Defaults to 512.</summary>
    [FromQuery]
    public uint Size { get; set; }

    public override Task<Result<FileResponse>> HandleAsync(CancellationToken ct = default)
    {
        var payload = string.IsNullOrWhiteSpace(Text) ? "https://pragmaticdesign.net/invoices" : Text;
        var target = Size == 0 ? 512u : Math.Clamp(Size, 64u, 2048u);

        // Native encode: QR → PNG bytes.
        var qrPng = QrCode.GeneratePng(payload, moduleSize: 8, margin: 2);

        // Native decode → resize → encode: prove the pipeline round-trips the generated image.
        using var pipeline = ImagePipeline.Load(qrPng, ImagingOptions.Strict);
        pipeline.Thumbnail(target, target, allowUpscale: true);
        var png = pipeline.Encode(ImageFormat.Png);

        FileResponse response = new(new MemoryStream(png), PngContentType, "invoice-qr.png");
        return Task.FromResult<Result<FileResponse>>(response);
    }
}
