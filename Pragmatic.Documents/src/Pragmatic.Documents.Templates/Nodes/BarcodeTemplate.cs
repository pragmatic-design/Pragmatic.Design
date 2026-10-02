namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Barcode template — Value can contain <c>{{expressions}}</c>.</summary>
public sealed record BarcodeTemplate : DocumentNodeTemplate
{
    public required string Value { get; init; }
    public Model.BarcodeType Type { get; init; } = Model.BarcodeType.QrCode;
    public double? Width { get; init; }
    public double? Height { get; init; }
}
