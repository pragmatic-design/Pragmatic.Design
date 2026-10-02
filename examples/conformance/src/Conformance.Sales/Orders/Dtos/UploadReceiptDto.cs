namespace Conformance.Sales.Dtos;

/// <summary>
///     What <c>UploadOrderAttachmentAction</c> returns: how much arrived, and with which fields.
/// </summary>
/// <param name="OrderId">The route.</param>
/// <param name="FileName">The name the client gave the file.</param>
/// <param name="Bytes">The bytes read from the stream — not the declared ones.</param>
/// <param name="Label">The required form field.</param>
/// <param name="FolderId">The typed form field.</param>
public sealed record UploadReceiptDto(Guid OrderId, string FileName, long Bytes, string Label, Guid FolderId);
