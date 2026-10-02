namespace Pragmatic.Documents.Model;

/// <summary>Barcode or QR code element.</summary>
public sealed record BarcodeNode : DocumentNode
{
    /// <summary>The value to encode.</summary>
    public required string Value { get; init; }

    /// <summary>Barcode type.</summary>
    public BarcodeType Type { get; init; } = BarcodeType.QrCode;

    /// <summary>Width in mm. Null = auto.</summary>
    public double? Width { get; init; }

    /// <summary>Height in mm. Null = auto.</summary>
    public double? Height { get; init; }
}
